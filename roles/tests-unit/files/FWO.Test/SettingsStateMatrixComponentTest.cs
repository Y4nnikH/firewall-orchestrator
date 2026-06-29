using FWO.Data.Workflow;
using FWO.Services.Workflow;
using FWO.Ui.Pages.Settings;
using NUnit.Framework;
using static FWO.Test.WorkflowConfigurationComponentTestSupport;

namespace FWO.Test
{
    [TestFixture]
    internal class SettingsStateMatrixComponentTest
    {
        [Test]
        public void HasCompleteMatrix_RequiresEveryWorkflowPhase()
        {
            SettingsStateMatrix component = new();
            GlobalStateMatrix matrix = new()
            {
                GlobalMatrix = new Dictionary<WorkflowPhases, StateMatrix>
                {
                    [WorkflowPhases.request] = new()
                }
            };
            SetField(component, "actStateMatrix", matrix);
            Assert.That(GetProperty<bool>(component, "HasCompleteMatrix"), Is.False);

            matrix.GlobalMatrix = Enum.GetValues<WorkflowPhases>().ToDictionary(phase => phase, _ => new StateMatrix());
            Assert.That(GetProperty<bool>(component, "HasCompleteMatrix"), Is.True);
        }

        [Test]
        public void RefreshConfigurationIds_OrdersActiveFirstThenByName()
        {
            SettingsStateMatrix component = new();
            SetField(component, "workflowConfigurations", new List<WorkflowConfiguration>
            {
                new() { Id = 3, Name = "Zulu" },
                new() { Id = 1, Name = "Beta", IsActive = true },
                new() { Id = 2, Name = "Alpha" }
            });

            Invoke(component, "RefreshConfigurationIds");

            Assert.That(GetField<List<int>>(component, "configurationIds"), Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void SelectedConfiguration_TracksSelectedIdAndReturnsNullForUnknownId()
        {
            SettingsStateMatrix component = new();
            WorkflowConfiguration selected = new() { Id = 4, Name = "Selected" };
            SetField(component, "workflowConfigurations", new List<WorkflowConfiguration> { selected });
            SetField(component, "selectedConfigurationId", 4);
            Assert.That(GetProperty<WorkflowConfiguration>(component, "SelectedConfiguration"), Is.SameAs(selected));

            SetField(component, "selectedConfigurationId", 99);
            Assert.That(GetNullableProperty<WorkflowConfiguration>(component, "SelectedConfiguration"), Is.Null);
        }

        [Test]
        public void GetPhaseTransitionGroups_UsesBindingOrderAndSkipsMissingGroups()
        {
            SettingsStateMatrix component = new();
            GlobalStateMatrix matrix = MatrixWithPhase(WorkflowPhases.approval);
            SetProperty(matrix, "PhaseBindings", new Dictionary<WorkflowPhases, StateMatrixPhaseBinding>
            {
                [WorkflowPhases.approval] = new(6, "Approval", [2, 99, 1], [])
            });
            SetField(component, "actStateMatrix", matrix);
            SetField(component, "transitionGroups", new List<StateMatrixTransitionGroup>
            {
                new() { Id = 1, Name = "First" },
                new() { Id = 2, Name = "Second" }
            });

            List<StateMatrixTransitionGroup> result = (List<StateMatrixTransitionGroup>)Invoke(component, "GetPhaseTransitionGroups", WorkflowPhases.approval)!;

            Assert.That(result.Select(group => group.Id), Is.EqualTo(new[] { 2, 1 }));
            Assert.That((List<StateMatrixTransitionGroup>)Invoke(component, "GetPhaseTransitionGroups", WorkflowPhases.request)!, Is.Empty);
        }

        [Test]
        public void TransitionGroupManagerActions_SelectOverviewOrSpecificGroup()
        {
            SettingsStateMatrix component = new();
            SetField(component, "transitionGroupIdToEdit", 5);
            Invoke(component, "OpenTransitionGroupManager");
            Assert.Multiple(() =>
            {
                Assert.That(GetFieldValue(component, "transitionGroupIdToEdit"), Is.Null);
                Assert.That(GetField<bool>(component, "EditTransitionGroupsMode"), Is.True);
            });

            SetField(component, "EditTransitionGroupsMode", false);
            Invoke(component, "EditTransitionGroup", new StateMatrixTransitionGroup { Id = 17 });
            Assert.Multiple(() =>
            {
                Assert.That(GetField<int?>(component, "transitionGroupIdToEdit"), Is.EqualTo(17));
                Assert.That(GetField<bool>(component, "EditTransitionGroupsMode"), Is.True);
            });
        }

        [Test]
        public void OpenLinkTransitionGroup_FiltersLinkedAndWrongPhaseGroups()
        {
            SettingsStateMatrix component = new();
            GlobalStateMatrix matrix = MatrixWithPhase(WorkflowPhases.request);
            SetProperty(matrix, "PhaseBindings", new Dictionary<WorkflowPhases, StateMatrixPhaseBinding>
            {
                [WorkflowPhases.request] = new(10, "Request", [1], [])
            });
            SetField(component, "actStateMatrix", matrix);
            SetField(component, "transitionGroups", new List<StateMatrixTransitionGroup>
            {
                new() { Id = 1, Name = "Already linked", Phase = "request" },
                new() { Id = 2, Name = "Generic" },
                new() { Id = 3, Name = "Request", Phase = "REQUEST" },
                new() { Id = 4, Name = "Approval", Phase = "approval" }
            });

            Invoke(component, "OpenLinkTransitionGroup", WorkflowPhases.request);

            Assert.Multiple(() =>
            {
                Assert.That(GetField<List<int>>(component, "linkableTransitionGroupIds"), Is.EqualTo(new[] { 2, 3 }));
                Assert.That(GetField<int>(component, "selectedTransitionGroupId"), Is.EqualTo(2));
                Assert.That(GetField<bool>(component, "LinkTransitionGroupMode"), Is.True);
            });
        }

        [Test]
        public void DerivedStateOverview_IsSparseAndSorted()
        {
            SettingsStateMatrix component = ComponentWithStates([1, 2, 3, 4], new Dictionary<int, int>
            {
                [3] = 4,
                [1] = 1,
                [2] = 4
            });

            List<KeyValuePair<int, int>> result = (List<KeyValuePair<int, int>>)Invoke(component, "GetDerivedStates", WorkflowPhases.request)!;

            Assert.Multiple(() =>
            {
                Assert.That(result.Select(mapping => mapping.Key), Is.EqualTo(new[] { 2, 3 }));
                Assert.That(Invoke(component, "HasDerivedStates", WorkflowPhases.request), Is.True);
            });
        }

        [Test]
        public void AddDerivedState_SupportsZeroStateIdAndSelectsDifferentTarget()
        {
            SettingsStateMatrix component = ComponentWithStates([0, 1], []);

            Invoke(component, "AddDerivedState", WorkflowPhases.request);

            Assert.Multiple(() =>
            {
                Assert.That(GetField<int>(component, "selectedDerivedFromStateId"), Is.Zero);
                Assert.That(GetField<int>(component, "selectedDerivedStateId"), Is.EqualTo(1));
                Assert.That(GetField<bool>(component, "EditDerivedStateMode"), Is.True);
            });
        }

        [Test]
        public void CanAddDerivedState_RequiresTwoStatesAndAnUnusedInputState()
        {
            SettingsStateMatrix oneState = ComponentWithStates([1], []);
            Assert.That(Invoke(oneState, "CanAddDerivedState", WorkflowPhases.request), Is.False);

            SettingsStateMatrix fullyMapped = ComponentWithStates([1, 2], new Dictionary<int, int> { [1] = 2, [2] = 1 });
            Assert.That(Invoke(fullyMapped, "CanAddDerivedState", WorkflowPhases.request), Is.False);

            fullyMapped = ComponentWithStates([1, 2], new Dictionary<int, int> { [1] = 1, [2] = 1 });
            Assert.That(Invoke(fullyMapped, "CanAddDerivedState", WorkflowPhases.request), Is.True);
        }

        [Test]
        public void CanSaveDerivedState_RejectsIdentityAndDuplicateInputMappings()
        {
            SettingsStateMatrix component = ComponentWithStates([1, 2, 3], new Dictionary<int, int> { [2] = 3 });
            SetField(component, "derivedStatePhase", WorkflowPhases.request);
            SetField(component, "selectedDerivedFromStateId", 1);
            SetField(component, "selectedDerivedStateId", 1);
            Assert.That(GetProperty<bool>(component, "CanSaveDerivedState"), Is.False);

            SetField(component, "selectedDerivedFromStateId", 2);
            SetField(component, "selectedDerivedStateId", 1);
            Assert.That(GetProperty<bool>(component, "CanSaveDerivedState"), Is.False);

            SetField(component, "selectedDerivedFromStateId", 1);
            SetField(component, "selectedDerivedStateId", 3);
            Assert.That(GetProperty<bool>(component, "CanSaveDerivedState"), Is.True);
        }

        [Test]
        public void SaveDerivedState_CanMoveMappingToAnotherInputState()
        {
            SettingsStateMatrix component = ComponentWithStates([1, 2, 3], new Dictionary<int, int> { [1] = 3 });
            Invoke(component, "EditDerivedState", WorkflowPhases.request, new KeyValuePair<int, int>(1, 3));
            SetField(component, "selectedDerivedFromStateId", 2);
            SetField(component, "selectedDerivedStateId", 3);

            Invoke(component, "SaveDerivedState");

            Dictionary<int, int> mappings = GetField<GlobalStateMatrix>(component, "actStateMatrix").GlobalMatrix[WorkflowPhases.request].DerivedStates;
            Assert.Multiple(() =>
            {
                Assert.That(mappings, Is.EqualTo(new Dictionary<int, int> { [2] = 3 }));
                Assert.That(GetField<bool>(component, "EditDerivedStateMode"), Is.False);
                Assert.That(GetFieldValue(component, "originalDerivedFromStateId"), Is.Null);
            });
        }

        [Test]
        public void RequestDeleteTransitionGroup_OpensOnlyForSingleUseGroup()
        {
            SettingsStateMatrix component = new();
            StateMatrixTransitionGroup shared = GroupWithUsages(1, 2);
            Invoke(component, "RequestDeleteTransitionGroup", shared);
            Assert.That(GetField<bool>(component, "DeleteTransitionGroupMode"), Is.False);

            StateMatrixTransitionGroup single = GroupWithUsages(3);
            Invoke(component, "RequestDeleteTransitionGroup", single);
            Assert.Multiple(() =>
            {
                Assert.That(GetField<bool>(component, "DeleteTransitionGroupMode"), Is.True);
                Assert.That(GetField<StateMatrixTransitionGroup>(component, "transitionGroupToDelete"), Is.SameAs(single));
            });
        }

        [Test]
        public void RequestUnlinkTransitionGroup_StoresPhaseAndGroup()
        {
            SettingsStateMatrix component = new();
            StateMatrixTransitionGroup group = new() { Id = 8 };

            Invoke(component, "RequestUnlinkTransitionGroup", WorkflowPhases.implementation, group);

            Assert.Multiple(() =>
            {
                Assert.That(GetField<WorkflowPhases>(component, "unlinkTransitionGroupPhase"), Is.EqualTo(WorkflowPhases.implementation));
                Assert.That(GetField<StateMatrixTransitionGroup>(component, "transitionGroupToUnlink"), Is.SameAs(group));
                Assert.That(GetField<bool>(component, "UnlinkTransitionGroupMode"), Is.True);
            });
        }

        [Test]
        public void InitSaveMatrix_OpensConfirmation()
        {
            SettingsStateMatrix component = new();
            Invoke(component, "InitSaveMatrix");
            Assert.That(GetField<bool>(component, "SaveMatrixMode"), Is.True);
        }

        private static SettingsStateMatrix ComponentWithStates(List<int> stateIds, Dictionary<int, int> derivedStates)
        {
            SettingsStateMatrix component = new();
            SetField(component, "stateIds", stateIds);
            SetField(component, "actStateMatrix", new GlobalStateMatrix
            {
                GlobalMatrix = new Dictionary<WorkflowPhases, StateMatrix>
                {
                    [WorkflowPhases.request] = new() { DerivedStates = derivedStates }
                }
            });
            return component;
        }

        private static GlobalStateMatrix MatrixWithPhase(WorkflowPhases phase) => new()
        {
            GlobalMatrix = new Dictionary<WorkflowPhases, StateMatrix> { [phase] = new() }
        };

        private static StateMatrixTransitionGroup GroupWithUsages(params int[] phaseMatrixIds) => new()
        {
            PhaseMatrixUsages = phaseMatrixIds.Select(id => new StateMatrixPhaseTransitionGroup { PhaseMatrixId = id }).ToList()
        };

        private static T? GetNullableProperty<T>(object instance, string propertyName) where T : class
        {
            System.Reflection.PropertyInfo property = instance.GetType().GetProperty(propertyName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                ?? throw new MissingMemberException(instance.GetType().FullName, propertyName);
            return property.GetValue(instance) as T;
        }
    }
}
