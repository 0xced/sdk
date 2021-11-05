// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using Microsoft.NET.TestFramework;
using Microsoft.NET.TestFramework.Assertions;
using Microsoft.NET.TestFramework.Commands;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static Microsoft.NET.Sdk.BlazorWebAssembly.Tests.ServiceWorkerAssert;

namespace Microsoft.NET.Sdk.BlazorWebAssembly.Tests
{
    public class WasmAOTPublishIntegrationTest : WasmPublishIntegrationTestBase
    {
        public WasmAOTPublishIntegrationTest(ITestOutputHelper log) : base(log) { }

        [Fact]
        public void AOT_Publish_InRelease_Works()
        {
            // Arrange
            var testAppName = "BlazorWasmWithLibrary";
            var testInstance = CreateAspNetSdkTestAssetWithAot(testAppName, "blazorwasm");
            File.WriteAllText(Path.Combine(testInstance.TestRoot, "blazorwasm", "App.razor.css"), "h1 { font-size: 16px; }");

            var publishCommand = new PublishCommand(Log, Path.Combine(testInstance.TestRoot, "blazorwasm"));
            publishCommand.Execute("/p:Configuration=Release").Should().Pass();

            var publishDirectory = publishCommand.GetOutputDirectory(DefaultTfm, "Release");

            var blazorPublishDirectory = Path.Combine(publishDirectory.ToString(), "wwwroot");

            var expectedFiles = new[]
            {
                "wwwroot/_framework/blazor.boot.json",
                "wwwroot/_framework/blazor.webassembly.js",
                "wwwroot/_framework/dotnet.wasm",
                "wwwroot/_framework/blazorwasm.dll",
                "wwwroot/_framework/System.Text.Json.dll",
                "wwwroot/_content/RazorClassLibrary/wwwroot/exampleJsInterop.js",
                "wwwroot/_content/RazorClassLibrary/styles.css",
                "wwwroot/index.html",
                "wwwroot/js/LinkedScript.js",
                "wwwroot/blazorwasm.styles.css",
                "wwwroot/css/app.css",
                "web.config"
            };

            publishDirectory.Should().HaveFiles(expectedFiles);
            
            new FileInfo(Path.Combine(blazorPublishDirectory, "css", "app.css")).Should().Contain(".publish");
        }

        [Fact]
        public void AOT_Publish_WithExistingWebConfig_Works()
        {
            // Arrange
            var testAppName = "BlazorWasmWithLibrary";
            var testInstance = CreateAspNetSdkTestAssetWithAot(testAppName, "blazorwasm");

            var webConfigContents = "test webconfig contents";
            File.WriteAllText(Path.Combine(testInstance.TestRoot, "blazorwasm", "web.config"), webConfigContents);

            var publishCommand = new PublishCommand(Log, Path.Combine(testInstance.TestRoot, "blazorwasm"));
            publishCommand.Execute("/p:Configuration=Release").Should().Pass();

            var publishDirectory = publishCommand.GetOutputDirectory(DefaultTfm, "Release");

            // Verify web.config
            new FileInfo(Path.Combine(publishDirectory.ToString(), "..", "web.config")).Should().Exist();
            new FileInfo(Path.Combine(publishDirectory.ToString(), "..", "web.config")).Should().Contain(webConfigContents);
        }

        [Fact]
        public void AOT_Publish_HostedAppWithScopedCss_VisualStudio()
        {
            // Simulates publishing the same way VS does by setting BuildProjectReferences=false.
            var testAppName = "BlazorHosted";
            var testInstance = CreateAspNetSdkTestAssetWithAot(testAppName, "blazorhosted", "blazorwasm");
            File.WriteAllText(Path.Combine(testInstance.TestRoot, "blazorwasm", "App.razor.css"), "h1 { font-size: 16px; }");

            // VS builds projects individually and then a publish with BuildDependencies=false, but building the main project is a close enough approximation for this test.
            var buildCommand = new BuildCommand(testInstance, "blazorwasm");
            buildCommand.Execute("/p:BuildInsideVisualStudio=true /p:Configuration=Release").Should().Pass();

            // Publish
            var publishCommand = new PublishCommand(Log, Path.Combine(testInstance.TestRoot, "blazorhosted"));
            publishCommand.Execute("/p:BuildProjectReferences=false /p:BuildInsideVisualStudio=true /p:Configuration=Release").Should().Pass();

            var publishDirectory = publishCommand.GetOutputDirectory(DefaultTfm);
            var blazorPublishDirectory = Path.Combine(publishDirectory.ToString(), "wwwroot");

            // Make sure the main project exists
            new FileInfo(Path.Combine(publishDirectory.ToString(), "blazorhosted.dll")).Should().Exist();

            // Verification for https://github.com/dotnet/aspnetcore/issues/19926. Verify binaries for projects
            // referenced by the Hosted project appear in the publish directory
            publishDirectory.Should().HaveFiles(new[]
            {
                "RazorClassLibrary.dll",
                "blazorwasm.dll"
            });

            publishDirectory.Should().HaveFiles(new[]
            {
                "wwwroot/_framework/blazor.boot.json",
                "wwwroot/_framework/blazor.webassembly.js",
                "wwwroot/_framework/dotnet.wasm",
                "wwwroot/_framework/blazorwasm.dll",
                "wwwroot/_framework/System.Text.Json.dll"
            });

            // Verify project references appear as static web assets
            // Also verify project references to the server project appear in the publish output
            publishDirectory.Should().HaveFiles(new[]
            {
                "wwwroot/_framework/RazorClassLibrary.dll",
                "RazorClassLibrary.dll"
            });

            // Verify static assets are in the publish directory
            publishDirectory.Should().HaveFiles(new[]
            {
                "wwwroot/index.html"
            });

            // Verify scoped css
            publishDirectory.Should().HaveFiles(new[]
            {
                "wwwroot/blazorwasm.styles.css"
            });

            // Verify static web assets from referenced projects are copied.
            publishDirectory.Should().HaveFiles(new[]
            {
                "wwwroot/_content/RazorClassLibrary/wwwroot/exampleJsInterop.js",
                "wwwroot/_content/RazorClassLibrary/styles.css",
            });

            // Verify web.config
            publishDirectory.Should().HaveFiles(new[]
            {
                "web.config"
            });

            VerifyBootManifestHashes(testInstance, Path.Combine(publishDirectory.ToString(), "wwwroot"));

            // Verify compression works
            publishDirectory.Should().HaveFiles(new[]
            {
                "wwwroot/_framework/dotnet.wasm.br",
                "wwwroot/_framework/blazorwasm.dll.br",
                "wwwroot/_framework/RazorClassLibrary.dll.br",
                "wwwroot/_framework/System.Text.Json.dll.br"
            });

            VerifyBootManifestHashes(testInstance, blazorPublishDirectory);
            VerifyServiceWorkerFiles(testInstance, blazorPublishDirectory,
                serviceWorkerPath: Path.Combine("serviceworkers", "my-service-worker.js"),
                serviceWorkerContent: "// This is the production service worker",
                assetsManifestPath: "custom-service-worker-assets.js");
        }

        private TestAsset CreateAspNetSdkTestAssetWithAot(
            string testAsset,
            params string[] appsToAOT) =>
            CreateAspNetSdkTestAsset(testAsset)
            .WithProjectChanges((project, document) =>
            {
                if (appsToAOT.Contains(Path.GetFileNameWithoutExtension(project)))
                {
                    document.Descendants("PropertyGroup").First().Add(new XElement("RunAOTCompilation", "true"));
                }
            });
    }
}
