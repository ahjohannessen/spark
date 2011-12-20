// Copyright 2008-2009 Louis DeJardin - http://whereslou.com
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// 
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Microsoft.CSharp;
using System.Linq;

namespace Spark.Compiler
{
    public class BatchCompiler
    {
        public string OutputAssembly { get; set; }

        public Assembly Compile(bool debug, string languageOrExtension, params string[] sourceCode)
        {
            var language = languageOrExtension;
            if (CodeDomProvider.IsDefinedLanguage(languageOrExtension) == false &&
                CodeDomProvider.IsDefinedExtension(languageOrExtension))
            {
                language = CodeDomProvider.GetLanguageFromExtension(languageOrExtension);
            }

        	CodeDomProvider codeProvider;
            CompilerParameters compilerParameters;
            
            if (ConfigurationManager.GetSection("system.codedom") != null)
            {
                var compilerInfo = CodeDomProvider.GetCompilerInfo(language);
                codeProvider = compilerInfo.CreateProvider();
                compilerParameters = compilerInfo.CreateDefaultCompilerParameters();
            }
            else
            {
                if (!language.Equals("c#", StringComparison.OrdinalIgnoreCase) && 
                    !language.Equals("cs", StringComparison.OrdinalIgnoreCase) && 
                    !language.Equals("csharp", StringComparison.OrdinalIgnoreCase))
                {
                    throw new CompilerException(
                        string.Format("When running the {0} in an AppDomain without a system.codedom config section only the csharp language is supported. This happens if you are precompiling your views.", 
                            typeof(BatchCompiler).FullName));
                }

                var compilerVersion = GetCompilerVersion();
                
                var providerOptions = new Dictionary<string, string> { { "CompilerVersion", compilerVersion } };
                codeProvider = new CSharpCodeProvider(providerOptions);
                compilerParameters = new CompilerParameters();
            }

            compilerParameters.TreatWarningsAsErrors = false;
            var extension = codeProvider.FileExtension;
			
			var assemblies = AppDomain.CurrentDomain.GetAssemblies();			
			var refAssemblies = assemblies.SelectMany(x => x.GetReferencedAssemblies());
			var refsUseMscorlib = refAssemblies.Any(x => x.FullName.Contains("mscorlib"));
			
            foreach (var assembly in assemblies)
            {

                if (assembly.IsDynamic() || assembly.FullName.Contains("mscorlib") && refsUseMscorlib)
                    continue;

                compilerParameters.ReferencedAssemblies.Add(assembly.Location);
            }

            CompilerResults compilerResults;
            var basePath = AppDomain.CurrentDomain.SetupInformation.DynamicBase ?? Path.GetTempPath();
            if (debug)
            {
                compilerParameters.IncludeDebugInformation = true;

                var baseFile = Path.Combine(basePath, Guid.NewGuid().ToString("n"));

                var codeFiles = new List<string>();
                int fileCount = 0;
                foreach (string sourceCodeItem in sourceCode)
                {
                    ++fileCount;
                    var codeFile = baseFile + "-" + fileCount + "." + extension;
                    using (var stream = new FileStream(codeFile, FileMode.Create, FileAccess.Write))
                    {
                        using (var writer = new StreamWriter(stream))
                        {
                            writer.Write(sourceCodeItem);
                        }
                    }
                    codeFiles.Add(codeFile);
                }

                if (!string.IsNullOrEmpty(OutputAssembly))
                {
                    compilerParameters.OutputAssembly = Path.Combine(basePath, OutputAssembly);
                }
                else
                {
                    compilerParameters.OutputAssembly = baseFile + ".dll";
                }
                compilerResults = codeProvider.CompileAssemblyFromFile(compilerParameters, codeFiles.ToArray());
            }
            else
            {
                if (!string.IsNullOrEmpty(OutputAssembly))
                {
                    compilerParameters.OutputAssembly = Path.Combine(basePath, OutputAssembly);
                }
                else
                {
                    // This should result in the assembly being loaded without keeping the file on disk
                    compilerParameters.GenerateInMemory = true;
                }

                compilerResults = codeProvider.CompileAssemblyFromSource(compilerParameters, sourceCode);
            }

            if (compilerResults.Errors.HasErrors)
            {
                var sb = new StringBuilder();
                sb.AppendLine("Dynamic view compilation failed.");

                foreach (CompilerError err in compilerResults.Errors)
                {
                    sb.AppendFormat("{4}({0},{1}): {2} {3}: ", err.Line, err.Column, err.IsWarning ? "warning" : "error", err.ErrorNumber, err.FileName);
                    sb.AppendLine(err.ErrorText);
                }

                sb.AppendLine();
                foreach (var sourceCodeItem in sourceCode)
                {
                    using (var reader = new StringReader(sourceCodeItem))
                    {
                        for (int lineNumber = 1; ; ++lineNumber)
                        {
                            var line = reader.ReadLine();
                            if (line == null)
                                break;
                            sb.Append(lineNumber).Append(' ').AppendLine(line);
                        }
                    }
                }
                throw new BatchCompilerException(sb.ToString(), compilerResults);
            }

            return compilerResults.CompiledAssembly;
        }
        
        private static string GetCompilerVersion()
        {
            return "v4.0";
        }
    }

    public static class AssemblyExtentions
    {
        public static bool IsDynamic(this Assembly assembly)
        {
            return assembly is AssemblyBuilder
                   || assembly.ManifestModule.GetType().Namespace == "System.Reflection.Emit" //.Net 4 specific
                   || assembly.HasNoLocation();
        }

        private static bool HasNoLocation(this Assembly assembly)
        {
            bool result;
            try
            {
                result = string.IsNullOrEmpty(assembly.Location);
            }
            catch (NotSupportedException)
            {
                return true;
            }
            return result;
        }
    }

}