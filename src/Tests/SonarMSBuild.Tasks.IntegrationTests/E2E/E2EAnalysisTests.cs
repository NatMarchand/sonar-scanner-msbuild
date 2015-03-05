﻿//-----------------------------------------------------------------------
// <copyright file="E2EAnalysisTests.cs" company="SonarSource SA and Microsoft Corporation">
//   (c) SonarSource SA and Microsoft Corporation.  All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

using Microsoft.Build.Construction;
using Microsoft.Build.Execution;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sonar.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TestUtilities;


namespace SonarMSBuild.Tasks.IntegrationTests.E2E
{

    /* Tests:

        * clashing project names -> separate folders
        * Project-level FxCop settings overridden
        * Project types: web, class, 
        * Project languages: C#, VB, C++???
        * Handling of missing Guids
        * Output dir is not set
        
    */

    [TestClass]
    [DeploymentItem("LinkedFiles\\Sonar.Integration.v0.1.targets")]
    public class E2EAnalysisTests
    {
        private const string ExpectedCompileListFileName = "CompileList.txt";

        public TestContext TestContext { get; set; }

        #region Tests

        [TestMethod]
        public void E2E_OutputFolderStructure()
        {
            // Checks the output folder structure is correct for a simple solution

            // Arrange
            string rootInputFolder = TestUtils.CreateTestSpecificFolder(this.TestContext, "Inputs");
            string rootOutputFolder = TestUtils.CreateTestSpecificFolder(this.TestContext, "Outputs");

            ProjectDescriptor project1 = new ProjectDescriptor()
            {
                ProjectName= "nonTestProject",
                ProjectGuid = Guid.NewGuid(),
                IsTestProject = false,
                ParentDirectoryPath = rootInputFolder,
                ProjectFolderName ="nonTestProjectDir",
                ProjectFileName = "nonTestProject.csproj"
            };

            ProjectRootElement project1Root = InitializeProject(project1, rootOutputFolder, null);

            BuildLogger logger = new BuildLogger();

            // Act
            BuildResult result = BuildProject(project1Root, logger);

            // Assert
            BuildUtilities.AssertTargetSucceeded(result, TargetConstants.DefaultBuildTarget);

            logger.AssertTargetExecuted(TargetConstants.WriteSonarProjectDataTargetName);

            // Check expected folder structure exists
            CheckRootOutputFolder(rootOutputFolder);

            // Check expected project outputs
            Assert.AreEqual(1, Directory.EnumerateDirectories(rootOutputFolder).Count(), "Only expecting one child directory to exist under the root analysis output folder");
            ProjectInfoAssertions.AssertProjectInfoExists(rootOutputFolder, project1.FullFilePath);

            string projectDir = Directory.EnumerateDirectories(rootOutputFolder).FirstOrDefault();
            Assert.IsFalse(string.IsNullOrEmpty(projectDir), "No project directories were created");

            // Specify the expected analysis results
            project1.AddAnalysisResult(AnalysisType.ManagedCompilerInputs.ToString(), Path.Combine(projectDir, ExpectedCompileListFileName));

            CheckProjectOutputFolder(project1, projectDir);
        }

        [TestMethod]
        [Description("If the output folder is not set our custom targets should not be executed")]
        public void E2E_FxCop_OutputFolderNotSet()
        {
            // Arrange
            string rootInputFolder = TestUtils.CreateTestSpecificFolder(this.TestContext, "Inputs");
            string rootOutputFolder = TestUtils.CreateTestSpecificFolder(this.TestContext, "Outputs");

            ProjectDescriptor descriptor = new ProjectDescriptor()
            {
                ProjectName = "FxCopProject",
                ProjectGuid = Guid.NewGuid(),
                IsTestProject = false,
                ParentDirectoryPath = rootInputFolder,
                ProjectFolderName = "FxCopProjectDir",
                ProjectFileName = "FxCopProjectDir.csproj"
            };

            // Don't set the output folder
            ProjectRootElement project1Root = InitializeProject(descriptor, null, null);

            BuildLogger logger = new BuildLogger();

            // 1. No code analysis properties
            BuildResult result = BuildProject(project1Root, logger);
            BuildUtilities.AssertTargetSucceeded(result, TargetConstants.DefaultBuildTarget);

            logger.AssertTargetNotExecuted(TargetConstants.SonarOverrideFxCopSettingsTarget);
            logger.AssertTargetNotExecuted(TargetConstants.SonarSetFxCopResultsTarget);

            AssertFxCopNotExecuted(logger);

            ProjectInfoAssertions.AssertNoProjectInfoFilesExists(rootOutputFolder);
        }

        [TestMethod]
        [Description("FxCop analysis should not be run if the output folder is set but a custom ruleset isn't specified")]
        public void E2E_FxCop_OutputFolderSet_SonarRulesetNotSpecified()
        {
            // Arrange
            string rootInputFolder = TestUtils.CreateTestSpecificFolder(this.TestContext, "Inputs");
            string rootOutputFolder = TestUtils.CreateTestSpecificFolder(this.TestContext, "Outputs");

            ProjectDescriptor descriptor = new ProjectDescriptor()
            {
                ProjectName = "FxCopProject",
                ProjectGuid = Guid.NewGuid(),
                IsTestProject = false,
                ParentDirectoryPath = rootInputFolder,
                ProjectFolderName = "FxCopProjectDir",
                ProjectFileName = "FxCopProjectDir.csproj"
            };

            // Set the output folder but not the config folder
            string fxCopLogFile = Path.Combine(rootInputFolder, "FxCopResults.xml");
            Dictionary<string, string> preImportProperties = new Dictionary<string, string>();
            preImportProperties["RunCodeAnalysis"] = "true";
            preImportProperties["CodeAnalysisLogFile"] = fxCopLogFile;
            ProjectRootElement project1Root = InitializeProject(descriptor, rootOutputFolder, preImportProperties);

            BuildLogger logger = new BuildLogger();

            // Act
            BuildResult result = BuildProject(project1Root, logger);

            // Assert
            BuildUtilities.AssertTargetSucceeded(result, TargetConstants.DefaultBuildTarget);

            logger.AssertTargetExecuted(TargetConstants.SonarOverrideFxCopSettingsTarget); // output folder is set so this should be executed
            logger.AssertTargetNotExecuted(TargetConstants.SonarSetFxCopResultsTarget);

            AssertFxCopNotExecuted(logger);
            Assert.IsFalse(File.Exists(fxCopLogFile), "FxCop log file should not have been produced");

            ProjectInfo projectInfo = ProjectInfoAssertions.AssertProjectInfoExists(rootOutputFolder, descriptor.FullFilePath);
            ProjectInfoAssertions.AssertAnalysisResultDoesNotExists(projectInfo, AnalysisType.FxCop.ToString());
        }

        [TestMethod]
        [Description("FxCop analysis should not be run if the output folder is set but the custom ruleset couldn't be found")]
        public void E2E_FxCop_OutputFolderSet_SonarRulesetNotFound()
        {
            // Arrange
            string rootInputFolder = TestUtils.CreateTestSpecificFolder(this.TestContext, "Inputs");
            string rootOutputFolder = TestUtils.CreateTestSpecificFolder(this.TestContext, "Outputs");

            ProjectDescriptor descriptor = new ProjectDescriptor()
            {
                ProjectName = "FxCopProject",
                ProjectGuid = Guid.NewGuid(),
                IsTestProject = false,
                ParentDirectoryPath = rootInputFolder,
                ProjectFolderName = "FxCopProjectDir",
                ProjectFileName = "FxCopProjectDir.csproj"
            };

            // Set the output folder and config path
            string fxCopLogFile = Path.Combine(rootInputFolder, "FxCopResults.xml");
            Dictionary<string, string> preImportProperties = new Dictionary<string, string>();
            preImportProperties["RunCodeAnalysis"] = "true"; // our targets should override this value
            preImportProperties["CodeAnalysisLogFile"] = fxCopLogFile;
            preImportProperties["SonarConfigPath"] = rootInputFolder;
            ProjectRootElement projectRoot = InitializeProject(descriptor, rootOutputFolder, preImportProperties);

            BuildLogger logger = new BuildLogger();

            // Act
            BuildResult result = BuildProject(projectRoot, logger);

            // Assert
            BuildUtilities.AssertTargetSucceeded(result, TargetConstants.DefaultBuildTarget);

            logger.AssertTargetExecuted(TargetConstants.SonarOverrideFxCopSettingsTarget);  // output folder is set so this should be executed
            logger.AssertTargetNotExecuted(TargetConstants.SonarSetFxCopResultsTarget);
            
            // We expect the core FxCop *target* to have been started, but it should then be skipped
            // executing the FxCop *task* because the condition on the target is false
            // -> the FxCop output file should not be produced
            AssertFxCopNotExecuted(logger);

            Assert.IsFalse(File.Exists(fxCopLogFile), "FxCop log file should not have been produced");

            ProjectInfo projectInfo = ProjectInfoAssertions.AssertProjectInfoExists(rootOutputFolder, descriptor.FullFilePath);
            ProjectInfoAssertions.AssertAnalysisResultDoesNotExists(projectInfo, AnalysisType.FxCop.ToString());
        }

        [TestMethod]
        [Description("FxCop analysis should be run if the output folder is set and the ruleset can be found")]
        public void E2E_FxCop_AllConditionsMet()
        {
            // Arrange
            string rootInputFolder = TestUtils.CreateTestSpecificFolder(this.TestContext, "Inputs");
            string rootOutputFolder = TestUtils.CreateTestSpecificFolder(this.TestContext, "Outputs");

            ProjectDescriptor descriptor = new ProjectDescriptor()
            {
                ProjectName = "FxCopProject",
                ProjectGuid = Guid.NewGuid(),
                IsTestProject = false,
                ParentDirectoryPath = rootInputFolder,
                ProjectFolderName = "FxCopProjectDir",
                ProjectFileName = "FxCopProjectDir.csproj"
            };

            string fxCopLogFile = Path.Combine(rootInputFolder, "FxCopResults.xml");
            Dictionary<string, string> preImportProperties = new Dictionary<string, string>();
            preImportProperties["RunCodeAnalysis"] = "false";
            preImportProperties["CodeAnalysisLogFile"] = fxCopLogFile;
            preImportProperties["CodeAnalysisRuleset"] = "specifiedInProject.ruleset";

            preImportProperties["SonarConfigPath"] = rootInputFolder;
            CreateValidFxCopRuleset(rootInputFolder, "SonarAnalysis.ruleset");

            ProjectRootElement project1Root = InitializeProject(descriptor, rootOutputFolder, preImportProperties);

            BuildLogger logger = new BuildLogger();

            // Act
            BuildResult result = BuildProject(project1Root, logger);

            // Assert
            BuildUtilities.AssertTargetSucceeded(result, TargetConstants.DefaultBuildTarget);

            AssertAllFxCopTargetsExecuted(logger);
            Assert.IsTrue(File.Exists(fxCopLogFile), "FxCop log file should have been produced");

            ProjectInfo projectInfo = ProjectInfoAssertions.AssertProjectInfoExists(rootOutputFolder, descriptor.FullFilePath);
            ProjectInfoAssertions.AssertAnalysisResultExists(projectInfo, AnalysisType.FxCop.ToString(), fxCopLogFile);
        }

        #endregion

        #region Private methods

        /// <summary>
        /// Creates a project file on disk from the specified descriptor.
        /// Sets the Sonar output folder property, if specified.
        /// </summary>
        private ProjectRootElement InitializeProject(ProjectDescriptor descriptor, string sonarOutputFolder, IDictionary<string, string> preImportProperties)
        {
            ProjectRootElement projectRoot = BuildUtilities.CreateMsBuildProject(this.TestContext, descriptor, preImportProperties);

            // TODO: work out some way to automatically set the tools version depending on the version of VS being used
            projectRoot.ToolsVersion = "12.0"; // use this line for VS2013
            //project1Root.ToolsVersion = "14.0"; // use this line for VS2015.

            if (!string.IsNullOrWhiteSpace(sonarOutputFolder))
            {
                projectRoot.AddProperty(TargetProperties.SonarOutputPath, sonarOutputFolder);
            }
            projectRoot.Save(descriptor.FullFilePath);

            this.TestContext.AddResultFile(descriptor.FullFilePath);
            return projectRoot;
        }

        private static BuildResult BuildProject(ProjectRootElement projectRoot, BuildLogger logger, params string[] targets)
        {
            ProjectInstance projectInstance = new ProjectInstance(projectRoot);
            
            BuildResult result = BuildUtilities.BuildTarget(projectInstance, logger, targets);

            BuildUtilities.DumpProjectProperties(projectInstance, "Project properties post-build");

            return result;
        }

        /// <summary>
        /// Creates a valid FxCop ruleset in the specified location.
        /// The contents of the ruleset are not important for the tests; the only
        /// requirement is that it should allow the FxCop targets to execute correctly.
        /// </summary>
        private void CreateValidFxCopRuleset(string rootInputFolder, string fileName)
        {
            string fullPath = Path.Combine(rootInputFolder, fileName);

            string content = @"
<?xml version='1.0' encoding='utf-8'?>
<RuleSet Name='Empty ruleset' Description='Valid empty ruleset' ToolsVersion='12.0'>
<!--
  <Include Path='minimumrecommendedrules.ruleset' Action='Default' />

  <Rules AnalyzerId='Microsoft.Analyzers.ManagedCodeAnalysis' RuleNamespace='Microsoft.Rules.Managed'>
    <Rule Id='CA1008' Action='Warning' />
  </Rules>
-->
</RuleSet>";

            File.WriteAllText(fullPath, content);
            this.TestContext.AddResultFile(fullPath);
        }

        #endregion

        #region Assertions methods

        private void CheckRootOutputFolder(string rootOutputFolder)
        {
            Assert.IsTrue(Directory.Exists(rootOutputFolder), "Expected root output folder does not exist");

            int fileCount = Directory.GetFiles(rootOutputFolder, "*.*", SearchOption.TopDirectoryOnly).Count();
            Assert.AreEqual(0, fileCount, "Not expecting the top-level output folder to contain any files");
        }

        private void CheckProjectOutputFolder(ProjectDescriptor expected, string projectOutputFolder)
        {
            Assert.IsFalse(string.IsNullOrEmpty(projectOutputFolder), "Test error: projectOutputFolder should not be null/empty");
            Assert.IsTrue(Directory.Exists(projectOutputFolder), "Expected project folder does not exist: {0}", projectOutputFolder);

            // Check folder naming
            string folderName = Path.GetFileName(projectOutputFolder);
            Assert.IsTrue(folderName.StartsWith(expected.ProjectName), "Project output folder does not start with the project name. Expected: {0}, actual: {1}",
                expected.ProjectFolderName, folderName);

            // Check specific files
            CheckProjectInfo(expected, projectOutputFolder);
            CheckCompileList(expected, projectOutputFolder);

            // Check there are no other files
            List<string> allowedFiles = new List<string>(expected.AnalysisResults.Select(ar => ar.Location));
            allowedFiles.Add(Path.Combine(projectOutputFolder, FileConstants.ProjectInfoFileName));
            AssertNoAdditionalFilesInFolder(projectOutputFolder, allowedFiles.ToArray());
        }

        private void CheckCompileList(ProjectDescriptor expected, string projectOutputFolder)
        {
            string fullName = AssertFileExists(projectOutputFolder, ExpectedCompileListFileName);

            string[] actualFileNames = File.ReadAllLines(fullName);

            CollectionAssert.AreEquivalent(expected.ManagedSourceFiles ?? new string[] { }, actualFileNames, "Compile list file does not contain the expected entries");
        }

        private void CheckProjectInfo(ProjectDescriptor expected, string projectOutputFolder)
        {
            string fullName = AssertFileExists(projectOutputFolder, FileConstants.ProjectInfoFileName); // should always exist

            ProjectInfo actualProjectInfo = ProjectInfo.Load(fullName);

            ProjectInfo expectedProjectInfo = expected.CreateProjectInfo();
            TestUtilities.ProjectInfoAssertions.AssertExpectedValues(expectedProjectInfo, actualProjectInfo);
        }

        private string AssertFileExists(string projectOutputFolder, string fileName)
        {
            string fullPath = Path.Combine(projectOutputFolder, fileName);
            bool exists = this.CheckExistenceAndAddToResults(fullPath);

            Assert.IsTrue(exists, "Expected file does not exist: {0}", fullPath);
            return fullPath;
        }

        private void AssertFileDoesNotExist(string projectOutputFolder, string fileName)
        {
            string fullPath = Path.Combine(projectOutputFolder, fileName);
            bool exists = this.CheckExistenceAndAddToResults(fullPath);

            Assert.IsFalse(exists, "Not expecting file to exist: {0}", fullPath);
        }

        private bool CheckExistenceAndAddToResults(string fullPath)
        {
            bool exists = File.Exists(fullPath);
            if (exists)
            {
                this.TestContext.AddResultFile(fullPath);
            }
            return exists;
        }

        private static void AssertNoAdditionalFilesInFolder(string folderPath, params string[] allowedFileNames)
        {
            string[] files = Directory.GetFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly);
            IEnumerable<string> additionalFiles = files.Except(allowedFileNames);

            if (additionalFiles.Any())
            {
                Console.WriteLine("Additional file(s) in folder: {0}", folderPath);
                foreach (string additionalFile in additionalFiles)
                {
                    Console.WriteLine("\t{0}", additionalFile);
                }
                Assert.Fail("Additional files exist in the project output folder: {0}", folderPath);
            }

        }

        private void AssertAllFxCopTargetsExecuted(BuildLogger logger)
        {
            logger.AssertTargetExecuted(TargetConstants.SonarOverrideFxCopSettingsTarget);
            logger.AssertTargetExecuted(TargetConstants.SonarSetFxCopResultsTarget);

            // If the sonar FxCop targets are executed then we expect the FxCop
            // target and task to be executed too
            AssertFxCopExecuted(logger);
        }

        private void AssertFxCopExecuted(BuildLogger logger)
        {
            logger.AssertTargetExecuted(TargetConstants.FxCopTarget);
            logger.AssertTaskExecuted(TargetConstants.FxCopTask);
        }

        private void AssertFxCopNotExecuted(BuildLogger logger)
        {
            // FxCop has a "RunCodeAnalysis" target and a "CodeAnalysis" task: the target executes the task.
            // We are interested in whether the task is executed or not as that is what will actually produce
            // the output file (it's possible that the target will be executed, but that it will decide
            // to skip the task because the required conditions are not met).
            logger.AssertTaskNotExecuted(TargetConstants.FxCopTask);
        }

        #endregion

    }
}