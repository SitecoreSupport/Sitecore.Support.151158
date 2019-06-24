namespace Sitecore.Support.Modules.EmailCampaign.Messages
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Sitecore.Data.Items;
    using Sitecore.ExM.Framework.Diagnostics;
    using Sitecore.Modules.EmailCampaign;
    using Sitecore.Modules.EmailCampaign.Core;
    using Sitecore.Modules.EmailCampaign.Core.Links;
    using Sitecore.Modules.EmailCampaign.Core.Pipelines.GenerateLink;
    using Sitecore.Modules.EmailCampaign.Messages;

    public class WebPageMail : Sitecore.Modules.EmailCampaign.Messages.WebPageMail
    {
        private readonly WebPageMailSource curSource;

        private List<FileInMemory> _relativeFiles;

        protected WebPageMail(Item item)
            : this(item, new MessageBodyCache(GlobalSettings.MessageBodyMaxCacheSize))
        {
        }
        protected WebPageMail(Item item, [NotNull] MessageBodyCache messageBodyCache)
            : base(item, messageBodyCache)
        {

            this.curSource = Source as WebPageMailSource;

        }
        
        public new static WebPageMail FromItem(Item item)
        {
            if (IsCorrectMessageItem(item))
            {
                return new WebPageMail(item);
            }

            return null;
        }

        public override object Clone()
        {
            var newMessage = new WebPageMail(this.InnerItem);
            this.CloneFields(newMessage);
            return newMessage;
        }

        protected override string CorrectHtml(string html, bool preview)
        {
            HtmlHelper helper = new Sitecore.Support.Modules.EmailCampaign.Core.HtmlHelper(html);
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