using System;
using Spark.Configuration;
using System.Configuration;
using NUnit.Framework;
using Spark.Tests.Stubs;

namespace Spark.Tests.AppConfig
{
	[TestFixture]
    public class AppConfigIntegrationTester
    {
        [Test]
        public void CanLoadSparkSectionHandlerFromAppConfig()
        {
            var config = (SparkSectionHandler)ConfigurationManager.GetSection("spark");
            Assert.IsTrue(config.Compilation.Debug);
        	Assert.AreEqual(NullBehaviour.Strict, config.Compilation.NullBehaviour);
            Assert.AreEqual(1, config.Compilation.Assemblies.Count);
            Assert.AreEqual(typeof(StubSparkView).FullName, config.Pages.PageBaseType);
            Assert.AreEqual(1, config.Pages.Namespaces.Count);
        }
		
		[Test]
        public void ConfigSettingsInServiceContainerUsedByDefault()
        {
            var container = new SparkServiceContainer();
			
			var config = ConfigurationManager.GetSection("spark");
            var settings = container.GetService<ISparkViewEngine>().Settings;
            
			Assert.AreSame(config, settings);
        }
	}
}