using FluentAssertions;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using Xunit;

namespace ServiceBusExplorer.Tests
{
    public class AssemblyVersionShould
    {
        private const string CSharpProjectExtension = ".csproj";

        [Fact]
        public void BeSameAsSetInProject()
        {
            var assemblyNames = Directory.GetFiles(".", "ServiceBus*.*")
                    .Where(x=>x.EndsWith(".dll") || x.EndsWith(".exe"));

            var projectFiles = Directory.GetFiles("../../../../", $"*{CSharpProjectExtension}", SearchOption.AllDirectories);
            
            var xmlDocument = new XmlDocument();

            var expectedVersions = projectFiles.Select(x =>
            {
                xmlDocument.Load(x);
                var assemblyVersionNode = xmlDocument.SelectSingleNode("//AssemblyVersion");
                var assemblyNameNode = xmlDocument.SelectSingleNode("//AssemblyName");

                return new
                {
                    assemblyName = assemblyNameNode?.InnerText ?? ExtractProjectName(x),
                    assemblyVersion= assemblyVersionNode.InnerText
                };
            }).ToArray() ;

            var actualAssemblyVersions = assemblyNames.Select(x=>
            {
                var assembly = Assembly.LoadFrom(x);
                return new
                {
                    assemblyName = assembly.GetName().Name,
                    assemblyVersion = assembly.GetName().Version.ToString()
                };
            }).ToArray();

            // Every project under src is scanned, including ones this test project doesn't reference
            // (the .NET 10 Desktop app), so check that each assembly here matches its project.
            actualAssemblyVersions.Should().HaveCount(9).And.BeSubsetOf(expectedVersions);
        }

        private string ExtractProjectName(string x)
        {
            return new FileInfo(x).Name.Replace(CSharpProjectExtension, string.Empty);
        }
    }
}
