﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ASR.Interface;
using ASR.Reports.Logs;
using ASR.Reports.Scanners;
using Sitecore;
using Sitecore.Data;
using Sitecore.Data.Fields;
using Sitecore.Data.Items;
using Sitecore.Workflows;

namespace ASR.Reports.Items
{
    public class ItemViewer : BaseViewer
    {
        #region Fields

        public static string COLUMNS_PARAMETER = "columns";
        public static string HEADERS_PARAMETER = "headers";
        public static string MAX_LENGHT_PARAMETER = "maxlength";

        private int maxLength = -1;

        #endregion

        #region Public properties

        public string Language { get; set; }

        public string Version { get; set; }

        public int MaxLength
        {
            get
            {
                if (maxLength < 0)
                {
                    if (!int.TryParse(getParameter(MAX_LENGHT_PARAMETER), out maxLength))
                    {
                        maxLength = 100;
                    }
                }
                return maxLength;
            }
        }

        public override string[] AvailableColumns
        {
            get
            {
                return new string[]
                    {
                        "Guid",
                        "ChildrenCount",
                        "Created",
                        "CreatedBy",
                        "DisplayName",
                        "Name",
                        "Language",
                        "LockedBy",
                        "Owner",
                        "Path",
                        "Template",
                        "Unlocked",
                        "Updated",
                        "UpdatedBy",
                        "Version",
                        "Versions",
                        "Workflow",
                        "HasClones",
                        "IsClone",
                        "SourceItemPath"

                    };
            }
        }

        #endregion

        #region Public methods

        public override void Display(DisplayElement dElement)
        {
            var itemElement = ExtractItem(dElement);
            itemElement = GetCorrectLanguage(itemElement);
            itemElement = GetCorrectVersion(itemElement);
        
            if (itemElement == null)
            {
                return;
            }
            dElement.Value = itemElement.Uri.ToString();

            dElement.Header = itemElement.Name;

            foreach (var column in Columns)
            {
                if (!dElement.HasColumn(column.Header))
                {
                    var text = getColumnText(column.Name, itemElement);
                    if (text.StartsWith("|"))
                    {
                        var splitvalues = text.Substring(1).Split('|');

                        dElement.AddColumn(column.Header, splitvalues[0],splitvalues[1]);
                    }
                    else
                    {
                        dElement.AddColumn(column.Header, string.IsNullOrEmpty(text) ? itemElement[column.Name] : text);
                    }
                }
            }
            dElement.Icon = itemElement.Appearance.Icon;
        }

        protected virtual Item GetCorrectVersion(Item itemElement)
        {
            if (string.IsNullOrEmpty(Version)) return itemElement;

            switch (Version)
            {
                case "first" :
                    return itemElement.Database.GetItem(itemElement.ID, itemElement.Language, Sitecore.Data.Version.Parse(1));
                case "latest" :
                    return itemElement.Versions.GetLatestVersion();
                case "previous":
                    if (itemElement.Version.Number > 1)
                    {
                        return itemElement.Database.GetItem(itemElement.ID, itemElement.Language, Sitecore.Data.Version.Parse(itemElement.Version.Number - 1));
                    }
                    return itemElement;
                case "next":
                    if (!itemElement.Versions.IsLatestVersion())
                    {
                        return itemElement.Database.GetItem(itemElement.ID, itemElement.Language, Sitecore.Data.Version.Parse(itemElement.Version.Number + 1));
                    }
                    return itemElement;                
            }
            var version = itemElement.Versions.GetVersionNumbers().FirstOrDefault(v => v.ToString() == Version);

            if (version != null)
            {
                return itemElement.Database.GetItem(itemElement.ID, itemElement.Language, version);
            }
            
            return itemElement;
        }

        protected virtual Item GetCorrectLanguage(Item itemElement)
        {
            if (string.IsNullOrEmpty(Language)) return itemElement;

            var language = Sitecore.Globalization.Language.Parse(Language);

            if (language == null) return itemElement;

            return itemElement.Database.GetItem(itemElement.ID, language);
        }

        protected virtual Item ExtractItem(DisplayElement dElement)
        {
            var itemElement = dElement.Element as Item;

            if (itemElement == null)
            {
                if (dElement.Element is ID)
                {
                    itemElement = Sitecore.Context.ContentDatabase.GetItem((ID)dElement.Element);
                }
                else if (dElement.Element is WorkflowEventCustom)
                {
                    itemElement = ((WorkflowEventCustom)dElement.Element).Item;
                }
                else if (dElement.Element is AuditItem)
                {
                    itemElement = Database.GetItem(((AuditItem)dElement.Element).ItemUri);
                }
            }
            return itemElement;
        }

        #endregion

        #region Protected methods

        protected virtual string formatDateField(Item item, ID fieldID)
        {
            DateField field = item.Fields[fieldID];

            if (field == null && String.IsNullOrEmpty(field.Value)) return string.Empty;

            var formattingstring = "|{0}|{1}";
            string formattedvalue; 
                var dateTimeFormatInfo = CultureInfo.CurrentUICulture.DateTimeFormat;

                var format = GetDateFormat(dateTimeFormatInfo.ShortDatePattern);

                if (field.InnerField.TypeKey == "datetime")
                     formattedvalue = 
                        field.DateTime.ToString(string.Concat(format, " ", dateTimeFormatInfo.ShortTimePattern));
                else
                    formattedvalue = field.DateTime.ToString(format);

                return string.Format(formattingstring, formattedvalue, item[fieldID]);

          
        }

        protected virtual string getColumnText(string name, Item itemElement)
        {
            switch (name)
            {
                case "guid":
                    return itemElement.ID.ToString();

                case "name":
                    return itemElement.Name;

                case "displayname":
                    return itemElement.DisplayName;

                case "createdby":
                    return itemElement[FieldIDs.CreatedBy];

                case "updated":
                    return formatDateField(itemElement, FieldIDs.Updated);

                case "updatedby":
                    return itemElement[FieldIDs.UpdatedBy];

                case "created":
                    return formatDateField(itemElement, FieldIDs.Created);

                case "lockedby":
                    LockField lf = itemElement.Fields["__lock"];
                    var text = "unlocked";
                    if (lf != null)
                    {
                        if (!string.IsNullOrEmpty(lf.Owner))
                            text = lf.Owner + " " + lf.Date.ToString("dd/MM/yy HH:mm");
                    }
                    return text;
                case "template":
                    return itemElement.Template.Name;

                case "path":
                    return itemElement.Paths.FullPath;

                case "owner":
                    return itemElement[FieldIDs.Owner];

                case "workflow":
                    return getWorkflowInfo(itemElement);

                case "childrencount":
                    return itemElement.Children.Count.ToString();

                case "version":
                    return itemElement.Version.ToString();

                case "versions":
                    return itemElement.Versions.Count.ToString();

                case "language":
                    return itemElement.Language.CultureInfo.DisplayName;

                case "isclone":
                    return itemElement.IsClone.ToString();

                case "hasclones":
                    return itemElement.HasClones.ToString();

                case "sourceitempath":
                    if (itemElement.Source != null)
                    {
                        return itemElement.Source.Paths.FullPath.ToString();
                    }
                    return EmptyText;
              
                default:
                    return GetFriendlyFieldValue(name, itemElement);
            }
        }

        #endregion

        #region Private methods

        private string getWorkflowInfo(Item itemElement)
        {
            var sb = new StringBuilder();
            var iw = itemElement.State.GetWorkflow();
            if (iw != null)
            {
                sb.Append(iw.Appearance.DisplayName);
            }
            var ws = itemElement.State.GetWorkflowState();

            if (ws != null)
            {
                sb.AppendFormat(" ({0})", ws.DisplayName);
            }

            if (iw != null)
            {
                IEnumerable<WorkflowEvent> events = iw.GetHistory(itemElement).OrderByDescending(e => e.Date);
                var enumerator = events.GetEnumerator();
                if (enumerator.MoveNext())
                {
                    var span = DateTime.Now.Subtract(enumerator.Current.Date);
                    sb.AppendFormat(" for {0} days {1} hours {2} minutes", span.Days, span.Hours, span.Minutes);
                }
            }
            return sb.Length > 0 ? sb.ToString() : EmptyText;
        }

        protected virtual string GetFriendlyFieldValue(string name, Item itemElement)
        {
            // to allow forcing fields rather than properties, allow prepending the name with @
            name = name.TrimStart('@');
            var field = itemElement.Fields[name];
            if (field != null && !string.IsNullOrEmpty(field.Value))
            {
                switch (field.TypeKey)
                {
                    case "date":
                    case "datetime":
                        return formatDateField(itemElement, field.ID);
                    case "droplink":
                    case "droptree":
                    case "reference":
                    case "grouped droplink":
                        var lookupFld = (LookupField)field;
                        if (lookupFld.TargetItem != null)
                        {
                            return lookupFld.TargetItem.Name;
                        }
                        break;
                    case "checklist":
                    case "multilist":
                    case "multilist with search":
                    case "treelist":
                    case "treelistex":
                        var multilistField = (MultilistField)field;
                        var strBuilder = new StringBuilder();
                        foreach (var item in multilistField.GetItems())
                        {
                            strBuilder.AppendFormat("{0}, ", item.Name);
                        }
                        return StringUtil.Clip(strBuilder.ToString().TrimEnd(',', ' '), this.MaxLength, true);
                        break;
                    case "link":
                    case "general link":
                        var lf = new LinkField(field);
                        switch (lf.LinkType)
                        {
                            case "media":
                            case "internal":
                                if (lf.TargetItem != null)
                                {
                                    return lf.TargetItem.Paths.ContentPath;
                                }
                                return lf.Value == string.Empty ? "[undefined]" : "[broken link] " + lf.Value;
                            case "anchor":
                            case "mailto":
                            case "external":
                                return lf.Url;
                            default:
                                return lf.Text;
                        }
                    default:
                        return StringUtil.Clip(StringUtil.RemoveTags(field.Value), MaxLength, true);
                }
            }
            return EmptyText;
        }

        #endregion
    }
}
