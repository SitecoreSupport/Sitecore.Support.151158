namespace Sitecore.Support.Modules.EmailCampaign.Messages
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using Sitecore.ContentTesting;
    using Sitecore.Data.Items;
    using Sitecore.ExM.Framework.Diagnostics;
    using Sitecore.Modules.EmailCampaign;
    using Sitecore.Modules.EmailCampaign.Core;
    using Sitecore.Modules.EmailCampaign.Core.Links;
    using Sitecore.Modules.EmailCampaign.Core.Pipelines.GenerateLink;

    public class ABTestMessage : Sitecore.Modules.EmailCampaign.Messages.ABTestMessage
    {
        private List<FileInMemory> _relativeFiles;

        protected ABTestMessage(Item item)
            : this(item, CoreFactory.Instance, ContentTestingFactory.Instance)
        {
        }

        internal ABTestMessage(Item item, CoreFactory coreFactory, [NotNull] IContentTestingFactory contentTestingFactory)
            : base(item)
        {
        }

        public new static ABTestMessage FromItem(Item item)
        {
            return (item != null && IsCorrectMessageItem(item)) ? new ABTestMessage(item) : null;
        }

        public override object Clone()
        {
            var newMessage = new ABTestMessage(InnerItem);

            var isTestConfiguredField = typeof(Sitecore.Modules.EmailCampaign.Messages.ABTestMessage).GetField("_isTestConfigured",
                BindingFlags.NonPublic | BindingFlags.Instance);
            isTestConfiguredField.SetValue(newMessage, isTestConfiguredField.GetValue(this));

            var testContextField = typeof(Sitecore.Modules.EmailCampaign.Messages.ABTestMessage).GetField("_testContext",
                BindingFlags.NonPublic | BindingFlags.Instance);
            testContextField.SetValue(newMessage, testContextField.GetValue(this));

            CloneFields(newMessage);
            return newMessage;
        }

        protected override string CorrectHtml(string html, bool preview)
        {
            Sitecore.Support.Modules.EmailCampaign.Core.HtmlHelper helper = new Sitecore.Support.Modules.EmailCampaign.Core.HtmlHelper(html);
            helper.CleanHtml();
            DateTime utcNow = DateTime.UtcNow;
            helper.InsertStyleSheets();
            Util.TraceTimeDiff("Insert style sheets", utcNow);
            utcNow = DateTime.UtcNow;
            html = new LinksManager(helper.Html, LinkType.Href).Replace(delegate (string link) {
                GenerateLinkPipelineArgs args = new GenerateLinkPipelineArgs(link, this, preview, this.ManagerRoot.Settings.WebsiteSiteConfigurationName);
                this.PipelineHelper.RunPipeline("modifyHyperlink", args);
                return args.Aborted ? null : args.GeneratedUrl;
            });
            Util.TraceTimeDiff("Modify 'href' links", utcNow);
            utcNow = DateTime.UtcNow;
            html = Sitecore.Support.Modules.EmailCampaign.Core.HtmlHelper.EncodeSrc(html);
            Util.TraceTimeDiff("Encode 'src' links", utcNow);
            return html;
        }
        public override void CollectRelativeFiles(bool preview = false)
        {
            try
            {
                _relativeFiles = new List<FileInMemory>();

                if (Body == null)
                {
                    return;
                }

                var settings = ManagerRoot.Settings;
                if (settings.EmbedImages)
                {
                    var htmlHelper = new Sitecore.Support.Modules.EmailCampaign.Core.HtmlHelper(Body);
                    var baseUrl = GetBaseUrl(preview, settings);

                    htmlHelper.CollectRelativeFiles(baseUrl);

                    Body = htmlHelper.Html;
                    _relativeFiles = htmlHelper.RelativeFiles.Values.ToList();
                }
                else
                {
                    var startTime = DateTime.UtcNow;
                    var linksManager = new LinksManager(Body, LinkType.Src | LinkType.Css);
                    Body = linksManager.Replace(link =>
                    {
                        var args = new GenerateLinkPipelineArgs(link, this, preview, ManagerRoot.Settings.WebsiteSiteConfigurationName);
                        PipelineHelper.RunPipeline(Sitecore.Modules.EmailCampaign.Core.Constants.ModifyHyperlinkPipeline, args);
                        return args.Aborted ? null : args.GeneratedUrl;
                    });
                    Util.TraceTimeDiff("Modify 'src' links", startTime);
                }
            }
            catch (Exception e)
            {
                Warnings.Add(e.Message);
                Logger.Instance.LogError(e);
            }
        }

        public override List<FileInMemory> RelativeFiles
        {
            get
            {
                if (this._relativeFiles == null)
                {
                    this.CollectRelativeFiles(false);
                }
                return this._relativeFiles;
            }
        }
    }
}