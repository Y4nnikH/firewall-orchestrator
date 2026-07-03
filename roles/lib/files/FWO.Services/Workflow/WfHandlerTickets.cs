using FWO.Basics;
using FWO.Data;
using FWO.Data.Workflow;
using FWO.Logging;

namespace FWO.Services.Workflow
{
    public partial class WfHandler
    {
        public bool DisplayTicketMode = false;
        public bool EditTicketMode = false;
        public bool AddTicketMode = false;
        public bool DisplayPromoteTicketMode = false;
        public bool DisplaySaveTicketMode = false;

        // Tickets

        public async Task<WfTicket?> ResolveTicket(long ticketId)
        {
            WfTicket? ticket = null;
            if (dbAcc != null)
            {
                ticket = await dbAcc.FetchTicket(ticketId, userConfig.ReqOwnerBased ? AllOwners.ConvertAll(x => x.Id) : null, ApplyVisibilityRestrictions);
                if (ticket != null)
                {
                    SetTicketEnv(ticket);
                    return ticket;
                }
            }
            return null;
        }

        public async Task<string> HandleInjectedTicketId(WorkflowPhases phase, long ticketId)
        {
            WfTicket? ticket = await ResolveTicket(ticketId);
            if (ticket != null)
            {
                if (ticket.StateId < MasterStateMatrix.LowestEndState)
                {
                    await SelectTicket(ticket, ObjAction.edit, true);
                }
                else if (MasterStateMatrix.IsLastActivePhase)
                {
                    await SelectTicket(ticket, ObjAction.display, true);
                }
                else
                {
                    (WorkflowPhases newPhase, bool foundNewPhase) = await FindNewPhase(phase, ticket.StateId);
                    if (foundNewPhase)
                    {
                        return newPhase.ToString();
                    }
                }
            }
            return "";
        }

        public async Task<List<WfTicket>> GetOpenTickets(string taskType, int cutOffPeriod = 0, SchedulerInterval interval = SchedulerInterval.Days)
        {
            if (dbAcc != null)
            {
                DateTime cutOffDate = interval switch
                {
                    SchedulerInterval.Days => DateTime.Now.AddDays(-cutOffPeriod),
                    SchedulerInterval.Weeks => DateTime.Now.AddDays(-cutOffPeriod * GlobalConst.kDaysPerWeek),
                    SchedulerInterval.Months => DateTime.Now.AddMonths(-cutOffPeriod),
                    _ => throw new NotSupportedException("Time interval is not supported."),
                };
                return await dbAcc.GetTicketsByParameters(taskType, StateMatrix(taskType).LowestInputState, StateMatrix(taskType).LowestEndState, cutOffDate,
                    ApplyVisibilityRestrictions);
            }
            return [];
        }

        public async Task SelectTicket(WfTicket ticket, ObjAction action, bool reload = false)
        {
            if (ReloadTasks && reload && dbAcc != null)
            {
                WfTicket? refreshedTicket = await dbAcc.FetchTicket(ticket.Id, null, ApplyVisibilityRestrictions);
                if (refreshedTicket == null)
                {
                    return;
                }
                ticket = refreshedTicket;
                int ticketIndex = TicketList.FindIndex(x => x.Id == ticket.Id);
                if (ticketIndex >= 0)
                {
                    TicketList[ticketIndex] = ticket;
                }
            }
            SetTicketEnv(ticket);
            SetTicketOpt(action);
        }

        public void SetTicketEnv(WfTicket ticket)
        {
            ActTicket = ticket;
            ResetImplTaskList();
            ActStateMatrix = MasterStateMatrix;
        }

        public void SetTicketOpt(ObjAction action)
        {
            ResetTicketActions();
            DisplayTicketMode = action == ObjAction.display || action == ObjAction.edit || action == ObjAction.add;
            EditTicketMode = action == ObjAction.edit || action == ObjAction.add;
            AddTicketMode = action == ObjAction.add;
        }

        public void SetTicketPopUpOpt(ObjAction action)
        {
            DisplayPromoteTicketMode = action == ObjAction.displayPromote;
            DisplaySaveTicketMode = action == ObjAction.displaySaveTicket;
        }

        public void ResetTicketActions()
        {
            DisplayTicketMode = false;
            EditTicketMode = false;
            AddTicketMode = false;
            DisplayPromoteTicketMode = false;
            DisplaySaveTicketMode = false;
        }

        public async Task<long> SaveTicket(WfStatefulObject ticket)
        {
            try
            {
                if (dbAcc != null)
                {
                    ActTicket.StateId = ticket.StateId;
                    PrepareTicketData();

                    if (AddTicketMode)
                    {
                        // insert new ticket
                        ActTicket.CreationDate = DateTime.Now;
                        ActTicket.Requester = userConfig.User;
                        ActTicket = await dbAcc.AddTicketToDb(ActTicket);
                        TicketList.Add(ActTicket);
                    }
                    else
                    {
                        // Update existing ticket
                        ActTicket = await dbAcc.UpdateTicketInDb(ActTicket);
                        TicketList[TicketList.FindIndex(x => x.Id == ActTicket.Id)] = ActTicket;
                    }

                    // update of request tasks and creation of impl tasks may be necessary
                    bool requestTaskActionsChangedState = await UpdateRequestTasksFromTicket();

                    if (requestTaskActionsChangedState)
                    {
                        // check for further promotion (req tasks may be promoted)
                        await UpdateActTicketStateFromReqTasks();
                    }

                    ResetTicketActions();
                    return ActTicket.Id;
                }
            }
            catch (Exception exception)
            {
                DisplayMessageInUi(exception, userConfig.GetText("save_request"), "", true);
            }
            return 0;
        }

        private bool CanViewTicket(WfTicket ticket)
        {
            bool visible = WorkflowVisibilityHelper.CanAccessStatefulObject(ticket, MasterStateMatrix, userConfig.User.WorkflowVisibilityGroupIds,
                GetWorkflowExclusiveVisibilityGroupIds());
            LogVisibilityDecision("ticket", ticket.StateId, ticket.Id.ToString(), MasterStateMatrix, visible);
            return visible;
        }

        private bool CanViewImplTask(WfImplTask implTask)
        {
            StateMatrix stateMatrix = StateMatrix(implTask.TaskType);
            bool visible = WorkflowVisibilityHelper.CanAccessStatefulObject(implTask, stateMatrix, userConfig.User.WorkflowVisibilityGroupIds,
                GetWorkflowExclusiveVisibilityGroupIds());
            LogVisibilityDecision("impl-task", implTask.StateId, $"{implTask.TaskType}/{implTask.Id}", stateMatrix, visible);
            return visible;
        }

        private bool CanViewApproval(WfApproval approval, StateMatrix reqTaskMatrix)
        {
            bool visible = WorkflowVisibilityHelper.CanAccessStatefulObject(approval, reqTaskMatrix, userConfig.User.WorkflowVisibilityGroupIds,
                GetWorkflowExclusiveVisibilityGroupIds());
            LogVisibilityDecision("approval", approval.StateId, approval.Id.ToString(), reqTaskMatrix, visible);
            return visible;
        }

        private bool ApplyVisibilityRestrictions(WfTicket ticket)
        {
            bool ticketVisible = CanViewTicket(ticket);
            bool hadRequestTasks = ticket.Tasks.Count > 0;
            List<WfReqTask> visibleReqTasks = [];
            foreach (WfReqTask reqTask in ticket.Tasks)
            {
                StateMatrix reqTaskMatrix = StateMatrix(reqTask.TaskType);
                bool taskVisible = WorkflowVisibilityHelper.CanAccessStatefulObject(reqTask, reqTaskMatrix, userConfig.User.WorkflowVisibilityGroupIds,
                    GetWorkflowExclusiveVisibilityGroupIds());
                if (!taskVisible)
                {
                    continue;
                }

                reqTask.ImplementationTasks = [.. reqTask.ImplementationTasks.Where(CanViewImplTask)];
                reqTask.Approvals = [.. reqTask.Approvals.Where(approval => CanViewApproval(approval, reqTaskMatrix))];
                visibleReqTasks.Add(reqTask);
            }

            ticket.Tasks = visibleReqTasks;
            if (hadRequestTasks && ticket.Tasks.Count == 0)
            {
                Log.WriteDebug("Workflow visibility",
                    $"Denied ticket {ticket.Id} in state {ticket.StateId} because no visible request tasks remain. " +
                    $"user groups: [{string.Join(", ", userConfig.User.WorkflowVisibilityGroupIds)}], exclusive groups: [{string.Join(", ", GetWorkflowExclusiveVisibilityGroupIds())}]");
                return false;
            }
            return ticketVisible;
        }

        private void LogVisibilityDecision(string objectType, int stateId, string objectId, StateMatrix stateMatrix, bool visible)
        {
            if (visible)
            {
                return;
            }

            List<int> requiredGroupIds = stateMatrix.GetVisibilityGroupIds(stateId);
            HashSet<int> exclusiveGroupIds = GetWorkflowExclusiveVisibilityGroupIds();
            Log.WriteDebug("Workflow visibility",
                $"Denied {objectType} {objectId} in state {stateId}. Required groups: [{string.Join(", ", requiredGroupIds)}], " +
                $"user groups: [{string.Join(", ", userConfig.User.WorkflowVisibilityGroupIds)}], exclusive groups: [{string.Join(", ", exclusiveGroupIds)}]");
        }

        public async Task ConfAddCommentToTicket(string commentText)
        {
            WfComment comment = new()
            {
                Scope = WfObjectScopes.Ticket.ToString(),
                CreationDate = DateTime.Now,
                Creator = userConfig.User,
                CommentText = commentText
            };
            if (dbAcc != null)
            {
                long commentId = await dbAcc.AddCommentToDb(comment);
                if (commentId != 0)
                {
                    await dbAcc.AssignCommentToTicketInDb(ActTicket.Id, commentId);
                }
            }
            ActTicket.Comments.Add(new WfCommentDataHelper(comment) { });
        }

        private async Task<(WorkflowPhases, bool)> FindNewPhase(WorkflowPhases phase, int stateId)
        {
            bool foundNewPhase = false;
            if (apiConnection != null)
            {
                GlobalStateMatrix glbStateMatrix = GlobalStateMatrix.Create();
                await glbStateMatrix.Init(apiConnection, WfTaskType.master);
                bool cont = true;
                while (cont)
                {
                    bool newPhase = MasterStateMatrix.getNextActivePhase(ref phase);
                    if (newPhase)
                    {
                        foundNewPhase = true;
                    }
                    cont = stateId >= glbStateMatrix.GlobalMatrix[phase].LowestEndState && newPhase;
                }
            }
            return (phase, foundNewPhase);
        }

        private void PrepareTicketData()
        {
            if (ActTicket.Sanitize())
            {
                DisplayMessageInUi(null, userConfig.GetText("save_request"), userConfig.GetText("U0001"), true);
            }
            foreach (WfReqTask reqTask in ActTicket.Tasks)
            {
                if (reqTask.StateId < ActTicket.StateId)
                {
                    reqTask.StateId = ActTicket.StateId;
                }
            }

            if (ActTicket.Deadline == null)
            {
                int? tickDeadline = PrioList.FirstOrDefault(x => x.NumPrio == ActTicket.Priority)?.TicketDeadline;
                ActTicket.Deadline = tickDeadline != null && tickDeadline > 0 ? DateTime.Now.AddDays((int)tickDeadline) : null;
            }
        }
    }
}
