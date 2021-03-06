﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using aggregator;
using aggregator.Engine;
using Microsoft.TeamFoundation.Work.WebApi;
using Microsoft.TeamFoundation.Work.WebApi.Contracts;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using NSubstitute;
using NSubstitute.Core.Arguments;

using unittests_ruleng.TestData;
using Xunit;

namespace unittests_ruleng
{
    internal static class StringExtensions
    {
        internal static string[] Mince(this string ruleCode)
        {
            return ruleCode.Split(Environment.NewLine);
        }
    }

    public class RuleTests
    {
        private readonly IAggregatorLogger logger;
        private readonly WorkHttpClient workClient;
        private readonly WorkItemTrackingHttpClient witClient;
        private readonly TestClientsContext clientsContext;

        public RuleTests()
        {
            logger = Substitute.For<IAggregatorLogger>();

            clientsContext = new TestClientsContext();

            workClient = clientsContext.WorkClient;
            witClient = clientsContext.WitClient;
            witClient.ExecuteBatchRequest(default).ReturnsForAnyArgs(info => new List<WitBatchResponse>());
        }

        [Fact]
        public async Task HelloWorldRule_Succeeds()
        {
            int workItemId = 42;
            WorkItem workItem = new WorkItem
            {
                Id = workItemId,
                Fields = new Dictionary<string, object>
                {
                    { "System.WorkItemType", "Bug" },
                    { "System.Title", "Hello" },
                    { "System.TeamProject", clientsContext.ProjectName },
                }
            };
            witClient.GetWorkItemAsync(workItemId, expand: WorkItemExpand.All).Returns(workItem);
            string ruleCode = @"
return $""Hello { self.WorkItemType } #{ self.Id } - { self.Title }!"";
";

            var engine = new RuleEngine(logger, ruleCode.Mince(), SaveMode.Default, dryRun: true);
            string result = await engine.ExecuteAsync(clientsContext.ProjectId, workItem, clientsContext, CancellationToken.None);

            Assert.Equal("Hello Bug #42 - Hello!", result);
            await witClient.DidNotReceive().GetWorkItemAsync(Arg.Any<int>(), expand: Arg.Any<WorkItemExpand>());
        }

        [Fact]
        public async Task LanguageDirective_Succeeds()
        {
            int workItemId = 42;
            WorkItem workItem = new WorkItem
            {
                Id = workItemId,
                Fields = new Dictionary<string, object>
                {
                    { "System.WorkItemType", "Bug" },
                    { "System.Title", "Hello" },
                    { "System.TeamProject", clientsContext.ProjectName },
                }
            };
            witClient.GetWorkItemAsync(workItemId, expand: WorkItemExpand.All).Returns(workItem);
            string ruleCode = @".lang=CS
return string.Empty;
";

            var engine = new RuleEngine(logger, ruleCode.Mince(), SaveMode.Default, dryRun: true);
            string result = await engine.ExecuteAsync(clientsContext.ProjectId, workItem, clientsContext, CancellationToken.None);

            Assert.Equal(EngineState.Success, engine.State);
            Assert.Equal(string.Empty, result);
            await witClient.DidNotReceive().GetWorkItemAsync(Arg.Any<int>(), expand: Arg.Any<WorkItemExpand>());
        }

        [Fact]
        public async Task LanguageDirective_Fails()
        {
            int workItemId = 42;
            WorkItem workItem = new WorkItem
            {
                Id = workItemId,
                Fields = new Dictionary<string, object>
                {
                    { "System.WorkItemType", "Bug" },
                    { "System.Title", "Hello" },
                    { "System.TeamProject", clientsContext.ProjectName },
                }
            };
            witClient.GetWorkItemAsync(workItemId, expand: WorkItemExpand.All).Returns(workItem);
            string ruleCode = @".lang=WHAT
return string.Empty;
";

            var engine = new RuleEngine(logger, ruleCode.Mince(), SaveMode.Default, dryRun: true);
            string result = await engine.ExecuteAsync(clientsContext.ProjectId, workItem, clientsContext, CancellationToken.None);

            Assert.Equal(EngineState.Error, engine.State);
        }

        [Fact]
        public async Task Parent_Succeeds()
        {
            int workItemId2 = 2;
            WorkItem workItem = new WorkItem
            {
                Id = workItemId2,
                Fields = new Dictionary<string, object>
                {
                    { "System.WorkItemType", "Bug" },
                    { "System.Title", "Hello" },
                    { "System.TeamProject", clientsContext.ProjectName },
                },
                Relations = new List<WorkItemRelation>
                {
                    new WorkItemRelation
                    {
                        Rel = "System.LinkTypes.Hierarchy-Reverse",
                        Url = $"{clientsContext.WorkItemsBaseUrl}/1"
                    }
                },
            };
            witClient.GetWorkItemAsync(workItemId2, expand: WorkItemExpand.All).Returns(workItem);
            int workItemId1 = 1;
            witClient.GetWorkItemAsync(1, expand: WorkItemExpand.All).Returns(new WorkItem
            {
                Id = workItemId1,
                Fields = new Dictionary<string, object>
                {
                    { "System.WorkItemType", "User Story" },
                    { "System.TeamProject", clientsContext.ProjectName },
                },
                Relations = new List<WorkItemRelation>
                {
                    new WorkItemRelation
                    {
                        Rel = "System.LinkTypes.Hierarchy-Forward",
                        Url = FormattableString.Invariant($"{clientsContext.WorkItemsBaseUrl}/{workItemId2}")
                    }
                },
            });
            string ruleCode = @"
string message = """";
var parent = self.Parent;
if (parent != null)
{
    message = $""Parent is {parent.Id}"";
}
return message;
";

            var engine = new RuleEngine(logger, ruleCode.Mince(), SaveMode.Default, dryRun: true);
            string result = await engine.ExecuteAsync(clientsContext.ProjectId, workItem, clientsContext, CancellationToken.None);

            Assert.Equal("Parent is 1", result);
            await witClient.Received(1).GetWorkItemAsync(Arg.Is(workItemId1), expand: Arg.Is(WorkItemExpand.All));
        }

        [Fact]
        public async Task New_Succeeds()
        {
            int workItemId = 1;
            WorkItem workItem = new WorkItem
            {
                Id = workItemId,
                Fields = new Dictionary<string, object>
                {
                    { "System.WorkItemType", "User Story" },
                    { "System.Title", "Hello" },
                    { "System.TeamProject", clientsContext.ProjectName },
                }
            };
            witClient.GetWorkItemAsync(workItemId, expand: WorkItemExpand.All).Returns(workItem);
            string ruleCode = @"
var wi = store.NewWorkItem(""Task"");
wi.Title = ""Brand new"";
";

            var engine = new RuleEngine(logger, ruleCode.Mince(), SaveMode.Default, dryRun: true);
            string result = await engine.ExecuteAsync(clientsContext.ProjectId, workItem, clientsContext, CancellationToken.None);

            Assert.Null(result);
            logger.Received().WriteInfo($"Found a request for a new Task workitem in {clientsContext.ProjectName}");
            logger.Received().WriteWarning("Dry-run mode: no updates sent to Azure DevOps.");
            logger.Received().WriteInfo("Changes saved to Azure DevOps (mode Default): 1 created, 0 updated.");
        }

        [Fact]
        public async Task AddChild_Succeeds()
        {
            int workItemId = 1;
            WorkItem workItem = new WorkItem
            {
                Id = workItemId,
                Fields = new Dictionary<string, object>
                {
                    { "System.WorkItemType", "User Story" },
                    { "System.Title", "Hello" },
                    { "System.TeamProject", clientsContext.ProjectName },
                }
            };
            witClient.GetWorkItemAsync(workItemId, expand: WorkItemExpand.All).Returns(workItem);
            string ruleCode = @"
var parent = self;
var newChild = store.NewWorkItem(""Task"");
newChild.Title = ""Brand new"";
parent.Relations.AddChild(newChild);
";

            var engine = new RuleEngine(logger, ruleCode.Mince(), SaveMode.Default, dryRun: true);
            string result = await engine.ExecuteAsync(clientsContext.ProjectId, workItem, clientsContext, CancellationToken.None);

            Assert.Null(result);
            logger.Received().WriteInfo($"Found a request for a new Task workitem in {clientsContext.ProjectName}");
            logger.Received().WriteInfo($"Found a request to update workitem {workItemId} in {clientsContext.ProjectName}");
            logger.Received().WriteWarning("Dry-run mode: no updates sent to Azure DevOps.");
            logger.Received().WriteInfo("Changes saved to Azure DevOps (mode Default): 1 created, 1 updated.");
        }

        [Fact]
        public async Task TouchDescription_Succeeds()
        {
            int workItemId = 42;
            WorkItem workItem = new WorkItem
            {
                Id = workItemId,
                Fields = new Dictionary<string, object>
                {
                    { "System.Description", "Hello" },
                    { "System.TeamProject", clientsContext.ProjectName },
                }
            };
            witClient.GetWorkItemAsync(workItemId, expand: WorkItemExpand.All).Returns(workItem);
            string ruleCode = @"
self.Description = self.Description + ""."";
return self.Description;
";

            var engine = new RuleEngine(logger, ruleCode.Mince(), SaveMode.Default, dryRun: true);
            string result = await engine.ExecuteAsync(clientsContext.ProjectId, workItem, clientsContext, CancellationToken.None);

            Assert.Equal("Hello.", result);
            logger.Received().WriteInfo($"Found a request to update workitem {workItemId} in {clientsContext.ProjectName}");
            logger.Received().WriteWarning("Dry-run mode: no updates sent to Azure DevOps.");
            logger.Received().WriteInfo("Changes saved to Azure DevOps (mode Default): 0 created, 1 updated.");
        }

        [Fact]
        public async Task ReferenceDirective_Succeeds()
        {
            int workItemId = 42;
            WorkItem workItem = new WorkItem
            {
                Id = workItemId,
                Fields = new Dictionary<string, object>
                {
                    { "System.WorkItemType", "Bug" },
                    { "System.Title", "Hello" },
                    { "System.TeamProject", clientsContext.ProjectName },
                }
            };
            witClient.GetWorkItemAsync(workItemId, expand: WorkItemExpand.All).Returns(workItem);
            string ruleCode = @".r=System.Xml.XDocument
var doc = new System.Xml.Linq.XDocument();
return string.Empty;
";

            var engine = new RuleEngine(logger, ruleCode.Mince(), SaveMode.Default, dryRun: true);
            string result = await engine.ExecuteAsync(clientsContext.ProjectId, workItem, clientsContext, CancellationToken.None);

            Assert.Equal(EngineState.Success, engine.State);
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public async Task ImportDirective_Succeeds()
        {
            int workItemId = 42;
            WorkItem workItem = new WorkItem
            {
                Id = workItemId,
                Fields = new Dictionary<string, object>
                {
                    { "System.WorkItemType", "Bug" },
                    { "System.Title", "Hello" },
                    { "System.TeamProject", clientsContext.ProjectName },
                }
            };
            witClient.GetWorkItemAsync(workItemId, expand: WorkItemExpand.All).Returns(workItem);
            string ruleCode = @".import=System.Diagnostics
Debug.WriteLine(""test"");
return string.Empty;
";

            var engine = new RuleEngine(logger, ruleCode.Mince(), SaveMode.Default, dryRun: true);
            string result = await engine.ExecuteAsync(clientsContext.ProjectId, workItem, clientsContext, CancellationToken.None);

            Assert.Equal(EngineState.Success, engine.State);
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public async Task ImportDirective_Fail()
        {
            int workItemId = 42;
            WorkItem workItem = new WorkItem
            {
                Id = workItemId,
                Fields = new Dictionary<string, object>
                {
                    { "System.WorkItemType", "Bug" },
                    { "System.Title", "Hello" },
                    { "System.TeamProject", clientsContext.ProjectName },
                }
            };
            witClient.GetWorkItemAsync(workItemId, expand: WorkItemExpand.All).Returns(workItem);
            string ruleCode = @"
Debug.WriteLine(""test"");
return string.Empty;
";

            var engine = new RuleEngine(logger, ruleCode.Mince(), SaveMode.Default, dryRun: true);
            await Assert.ThrowsAsync<Microsoft.CodeAnalysis.Scripting.CompilationErrorException>(
                () => engine.ExecuteAsync(clientsContext.ProjectId, workItem, clientsContext, CancellationToken.None)
            );
        }

        [Fact]
        public async Task DeleteWorkItem()
        {
            int workItemId = 42;
            WorkItem workItem = new WorkItem
            {
                Id = workItemId,
                Fields = new Dictionary<string, object>
                {
                    { "System.WorkItemType", "Bug" },
                    { "System.Title", "Hello" },
                    { "System.TeamProject", clientsContext.ProjectName },
                }
            };
            witClient.GetWorkItemAsync(workItemId, expand: WorkItemExpand.All).Returns(workItem);
            string ruleCode = @"
Debug.WriteLine(""test"");
return string.Empty;
";

            var engine = new RuleEngine(logger, ruleCode.Mince(), SaveMode.Default, dryRun: true);
            await Assert.ThrowsAsync<Microsoft.CodeAnalysis.Scripting.CompilationErrorException>(
                () => engine.ExecuteAsync(clientsContext.ProjectId, workItem, clientsContext, CancellationToken.None)
            );
        }

        [Fact]
        public async Task HelloWorldRuleOnUpdate_Succeeds()
        {
            var workItem = ExampleTestData.WorkItem;
            var workItemUpdate = ExampleTestData.WorkItemUpdateFields;

            string ruleCode = @"
return $""Hello #{ selfChanges.WorkItemId } - Update { selfChanges.Id } changed Title from { selfChanges.Fields[""System.Title""].OldValue } to { selfChanges.Fields[""System.Title""].NewValue }!"";
";

            var engine = new RuleEngine(logger, ruleCode.Mince(), SaveMode.Default, dryRun: true);
            string result = await engine.ExecuteAsync(clientsContext.ProjectId, new WorkItemData(workItem, workItemUpdate), clientsContext, CancellationToken.None);

            Assert.Equal("Hello #22 - Update 3 changed Title from Initial Title to Hello!", result);
            await witClient.DidNotReceive().GetWorkItemAsync(Arg.Any<int>(), expand: Arg.Any<WorkItemExpand>());
        }

        [Fact]
        public async Task DocumentationRule_OnUpdateExample_Succeeds()
        {
            var workItem = ExampleTestData.WorkItem;
            var workItemUpdate = ExampleTestData.WorkItemUpdateFields;

            witClient.GetWorkItemAsync(workItem.Id.Value, expand: WorkItemExpand.All).Returns(workItem);
            string ruleCode = @"
            if (selfChanges.Fields.ContainsKey(""System.Title""))
            {
                var titleUpdate = selfChanges.Fields[""System.Title""];
                return $""Title was changed from '{titleUpdate.OldValue}' to '{titleUpdate.NewValue}'"";
            }
            else
            {
                return ""Title was not updated"";
            }
            ";

            var engine = new RuleEngine(logger, ruleCode.Mince(), SaveMode.Default, dryRun: true);
            string result = await engine.ExecuteAsync(clientsContext.ProjectId, new WorkItemData(workItem, workItemUpdate), clientsContext, CancellationToken.None);

            Assert.Equal("Title was changed from 'Initial Title' to 'Hello'", result);
            await witClient.DidNotReceive().GetWorkItemAsync(Arg.Any<int>(), expand: Arg.Any<WorkItemExpand>());
        }

        [Fact]
        public async Task CustomStringField_HasValue_Succeeds()
        {
            int workItemId = 42;
            WorkItem workItem = new WorkItem
            {
                Id = workItemId,
                Fields = new Dictionary<string, object>
                {
                    { "System.WorkItemType", "Bug" },
                    { "System.Title", "Hello" },
                    { "System.TeamProject", clientsContext.ProjectName },
                    { "MyOrg.CustomStringField", "some value" },
                }
            };
            witClient.GetWorkItemAsync(workItemId, expand: WorkItemExpand.All).Returns(workItem);
            string ruleCode = @"
var customField = self.GetFieldValue<string>(""MyOrg.CustomStringField"", ""MyDefault"");
return customField;
";

            var engine = new RuleEngine(logger, ruleCode.Mince(), SaveMode.Default, dryRun: true);
            string result = await engine.ExecuteAsync(clientsContext.ProjectId, workItem, clientsContext, CancellationToken.None);
            Assert.Equal("some value", result);
        }

        [Fact]
        public async Task CustomStringField_NoValue_ReturnsDefault()
        {
            int workItemId = 42;
            WorkItem workItem = new WorkItem
            {
                Id = workItemId,
                Fields = new Dictionary<string, object>
                {
                    { "System.WorkItemType", "Bug" },
                    { "System.Title", "Hello" },
                    { "System.TeamProject", clientsContext.ProjectName },
                }
            };
            witClient.GetWorkItemAsync(workItemId, expand: WorkItemExpand.All).Returns(workItem);
            string ruleCode = @"
var customField = self.GetFieldValue<string>(""MyOrg.CustomStringField"", ""MyDefault"");
return customField;
";

            var engine = new RuleEngine(logger, ruleCode.Mince(), SaveMode.Default, dryRun: true);
            string result = await engine.ExecuteAsync(clientsContext.ProjectId, workItem, clientsContext, CancellationToken.None);
            Assert.Equal("MyDefault", result);
        }

        [Fact]
        public async Task CustomNumericField_HasValue_Succeeds()
        {
            int workItemId = 42;
            WorkItem workItem = new WorkItem
            {
                Id = workItemId,
                Fields = new Dictionary<string, object>
                {
                    { "System.WorkItemType", "Bug" },
                    { "System.Title", "Hello" },
                    { "System.TeamProject", clientsContext.ProjectName },
                    { "MyOrg.CustomNumericField", 42 },
                }
            };
            witClient.GetWorkItemAsync(workItemId, expand: WorkItemExpand.All).Returns(workItem);
            string ruleCode = @"
var customField = self.GetFieldValue<decimal>(""MyOrg.CustomNumericField"", 3.0m);
return customField.ToString(""N"");
";

            var engine = new RuleEngine(logger, ruleCode.Mince(), SaveMode.Default, dryRun: true);
            string result = await engine.ExecuteAsync(clientsContext.ProjectId, workItem, clientsContext, CancellationToken.None);
            Assert.Equal("42.00", result);
        }

        [Fact]
        public async Task CustomNumericField_NoValue_ReturnsDefault()
        {
            int workItemId = 42;
            WorkItem workItem = new WorkItem
            {
                Id = workItemId,
                Fields = new Dictionary<string, object>
                {
                    { "System.WorkItemType", "Bug" },
                    { "System.Title", "Hello" },
                    { "System.TeamProject", clientsContext.ProjectName },
                }
            };
            witClient.GetWorkItemAsync(workItemId, expand: WorkItemExpand.All).Returns(workItem);
            string ruleCode = @"
var customField = self.GetFieldValue<decimal>(""MyOrg.CustomNumericField"", 3.0m);
return customField.ToString(""N"");
";

            var engine = new RuleEngine(logger, ruleCode.Mince(), SaveMode.Default, dryRun: true);
            string result = await engine.ExecuteAsync(clientsContext.ProjectId, workItem, clientsContext, CancellationToken.None);
            Assert.Equal("3.00", result);
        }

        [Fact]
        public async Task DocumentationRule_BacklogWorkItemsActivateParent_Succeeds()
        {
            var workItemUS = ExampleTestData.BacklogUserStoryActive;
            var workItemFeature = ExampleTestData.BacklogFeatureOneChild;

            var workItemUpdate = ExampleTestData.WorkItemUpdateFields;

            witClient.GetWorkItemTypeStatesAsync(clientsContext.ProjectName, Arg.Any<string>()).Returns(ExampleTestData.WorkItemStateColorDefault.ToList());
            workClient.GetProcessConfigurationAsync(clientsContext.ProjectName).Returns(ExampleTestData.ProcessConfigDefaultAgile);
            witClient.GetWorkItemAsync(workItemFeature.Id.Value, expand: WorkItemExpand.All).Returns(workItemFeature);

            var engine = new RuleEngine(logger, ExampleRuleCode.ActivateParent, SaveMode.Default, dryRun: true);
            string result = await engine.ExecuteAsync(clientsContext.ProjectId, new WorkItemData(workItemUS, workItemUpdate), clientsContext, CancellationToken.None);

            Assert.Equal("updated Parent Feature #1 to State='Active'", result);
        }

        [Fact]
        public async Task DocumentationRule_BacklogWorkItemsResolveParent_Succeeds()
        {
            var workItemFeature = ExampleTestData.BacklogFeatureOneChild;
            var workItemUS = ExampleTestData.BacklogUserStoryClosed;

            var workItemUpdate = ExampleTestData.WorkItemUpdateFields;

            witClient.GetWorkItemTypeStatesAsync(clientsContext.ProjectName, Arg.Any<string>()).Returns(ExampleTestData.WorkItemStateColorDefault.ToList());
            workClient.GetProcessConfigurationAsync(clientsContext.ProjectName).Returns(ExampleTestData.ProcessConfigDefaultAgile);
            witClient.GetWorkItemAsync(workItemFeature.Id.Value, expand: WorkItemExpand.All).Returns(workItemFeature);

            var engine = new RuleEngine(logger, ExampleRuleCode.ResolveParent, SaveMode.Default, dryRun: true);
            string result = await engine.ExecuteAsync(clientsContext.ProjectId, new WorkItemData(workItemUS, workItemUpdate), clientsContext, CancellationToken.None);

            Assert.Equal("updated Parent #1 to State='Resolved'", result);
        }

        [Fact]
        public async Task DocumentationRule_BacklogWorkItemsResolveParent_FailsDueToChildren()
        {
            var workItemFeature = ExampleTestData.BacklogFeatureTwoChildren;
            var workItemUS2 = ExampleTestData.BacklogUserStoryClosed;
            var workItemUS3= ExampleTestData.BacklogUserStoryActive;
            workItemUS3.Id = 3;

            var workItemUpdate = ExampleTestData.WorkItemUpdateFields;

            witClient.GetWorkItemTypeStatesAsync(clientsContext.ProjectName, Arg.Any<string>()).Returns(ExampleTestData.WorkItemStateColorDefault.ToList());
            workClient.GetProcessConfigurationAsync(clientsContext.ProjectName).Returns(ExampleTestData.ProcessConfigDefaultAgile);
            witClient.GetWorkItemAsync(workItemFeature.Id.Value, expand: WorkItemExpand.All).Returns(workItemFeature);
            witClient.GetWorkItemsAsync(Arg.Is<IEnumerable<int>>(ints => ints.Single() == workItemUS3.Id.Value), expand: WorkItemExpand.All).Returns(new List<WorkItem>() { workItemUS3 });

            var engine = new RuleEngine(logger, ExampleRuleCode.ResolveParent, SaveMode.Default, dryRun: true);
            string result = await engine.ExecuteAsync(clientsContext.ProjectId, new WorkItemData(workItemUS2, workItemUpdate), clientsContext, CancellationToken.None);

            Assert.Equal("Not all child work items <Removed> or <Completed>: #2=Closed,#3=Active", result);
        }
    }
}
