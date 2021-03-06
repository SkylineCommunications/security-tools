using Skyline.DataMiner.Automation;
using Skyline.DataMiner.Net.Messages;

using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ViewPermissions
{
    public class Script
    {
        public Script()
        {
        }

        private readonly List<ViewInfoEventMessage> allViews = new List<ViewInfoEventMessage>();

        public void Run(IEngine engine)
        {
            // Create the dialog box
            UIBuilder builder = new UIBuilder();

            // Configure the dialog box
            builder.RequireResponse = true;
            builder.RowDefs = "a;a;a;a;a;a;a;a;a;a";
            builder.ColumnDefs = "a;a";

            UIBlockDefinition viewLabel = new UIBlockDefinition();
            viewLabel.Type = UIBlockType.StaticText;
            viewLabel.Row = 0;
            viewLabel.Column = 0;
            viewLabel.Text = "Views:";
            builder.AppendBlock(viewLabel);

            UIBlockDefinition viewsList = new UIBlockDefinition();
            viewsList.Type = UIBlockType.DropDown;
            viewsList.Height = 20;
            viewsList.Row = 1;
            viewsList.Column = 0;
            viewsList.IsSorted = true;
            viewsList.IsRequired = true;
            viewsList.DestVar = nameof(viewsList);

            var views = GetViews(engine);

            if(!views.Any())
            {
                engine.ExitFail("Failed to fetch views");
            }

            // Sort the list
            views = views.OrderBy(v => v.ID);
            allViews.AddRange(views);

            foreach (var view in views)
            {
                // Add the ID for later usage
                viewsList.AddDropDownOption(view.ID.ToString(), view.Name);
            }

            builder.AppendBlock(viewsList);

            UIBlockDefinition btnShowAccess = new UIBlockDefinition();
            btnShowAccess.Type = UIBlockType.Button;
            btnShowAccess.Text = "Show View Access";
            btnShowAccess.Height = 20;
            btnShowAccess.Width = 150;
            btnShowAccess.Row = 2;
            btnShowAccess.Column = 0;
            btnShowAccess.DestVar = nameof(btnShowAccess);
            builder.AppendBlock(btnShowAccess);

            UIBlockDefinition validationState = new UIBlockDefinition();
            validationState.Type = UIBlockType.StaticText;
            validationState.Row = 3;
            validationState.Column = 0;
            builder.AppendBlock(validationState);

            bool isValid = false;

            do
            {
                // Display the dialog box
                var uiResults = engine.ShowUI(builder);

                if (uiResults == null)
                {
                    isValid = false;
                    continue;
                }

                string view = uiResults.GetString(nameof(viewsList));

                if(string.IsNullOrEmpty(view))
                {
                    isValid = false;
                    continue;
                }

                viewsList.InitialValue = view;

                bool showAccess = uiResults.WasButtonPressed(nameof(btnShowAccess));

                if(!showAccess)
                {
                    continue;
                }

                ShowViewAccess(engine, uiResults.GetString(nameof(viewsList)), validationState);
            }
            while (!isValid);
        }

        private void ShowViewAccess(IEngine engine, string id, UIBlockDefinition output)
        {
            var allGroups = GetAllGroups(engine);

            if(allGroups?.Any() == false)
            {
                engine.ExitFail("Failed to retrieve DataMiner groups");
            }

            if(!int.TryParse(id, out int viewId))
            {
                engine.ExitFail(id + " is not a valid view id.");
            }

            StringBuilder result = new StringBuilder();
            foreach (var group in allGroups)
            {
                bool hasMaxViewAccess = false;
                var rootView = group.Views.FirstOrDefault(v => v.ID == -1);
                DataMinerUserGroupView actualPermissions = null;

                if(rootView != null)
                {
                    hasMaxViewAccess = rootView.WriteAccess && rootView.Editable;

                    if(hasMaxViewAccess)
                    {
                        actualPermissions = rootView;
                    }
                }

                actualPermissions = group.Views.FirstOrDefault(v => v.ID == viewId);

                if (actualPermissions == null)
                {
                    // When a group has full permissions on the parent view it won't be in the list
                    actualPermissions = FindFirstParentView(viewId, group);

                    if (actualPermissions == null)
                    {
                        continue;
                    }
                }

                bool fullAccess = hasMaxViewAccess || (actualPermissions.WriteAccess && actualPermissions.Editable);
                string access = (fullAccess ? "full" : (actualPermissions.WriteAccess ? "write" : "config")) + " access";
                result.AppendLine("DataMiner Group '" + group.Name + "' has " + access);
            }

            output.Text = result.ToString();
        }

        /// <summary>
        /// Child view inherits the permissions from the first parent view.
        /// </summary>
        private DataMinerUserGroupView FindFirstParentView(int viewId, DataMinerUserGroup group)
        {
            int parentId = GetParentViewId(viewId);
            var parentView = group.Views.FirstOrDefault(v => v.ID == parentId);

            if(parentView != null || parentId == -1)
            {
                return parentView;
            }

            return FindFirstParentView(parentId, group);
        }

        private int GetParentViewId(int childId)
        {
            return allViews.FirstOrDefault(v => v.ID == childId)?.ParentId ?? -1;
        }

        private IEnumerable<DataMinerUserGroup> GetAllGroups(IEngine engine)
        {
            return (engine.SendSLNetSingleResponseMessage(new GetInfoMessage(InfoType.SecurityInfo)) as GetUserInfoResponseMessage)?.Groups;
        }

        private IEnumerable<ViewInfoEventMessage> GetViews(IEngine engine)
        {
            return engine.SendSLNetMessage(new GetInfoMessage(InfoType.ViewInfo)).Cast<ViewInfoEventMessage>();
        }
    }
}
