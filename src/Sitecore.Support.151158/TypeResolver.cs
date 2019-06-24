namespace Sitecore.Support.Modules.EmailCampaign.Core
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Web;
    using Sitecore.Data.Items;
    using Sitecore.Modules.EmailCampaign.Messages;

    public class TypeResolver : Sitecore.Modules.EmailCampaign.Core.TypeResolver
    {
        public override MessageItem GetCorrectMessageObject(Item item)
        {
            if (item == null)
            {
                return null;
            }

            if (ABTestMessage.IsCorrectMessageItem(item))
            {
                return Sitecore.Support.Modules.EmailCampaign.Messages.ABTestMessage.FromItem(item);
            }

            if (WebPageMail.IsCorrectMessageItem(item))
            {
                return Sitecore.Support.Modules.EmailCampaign.Messages.WebPageMail.FromItem(item);
            }

            if (HtmlMail.IsCorrectMessageItem(item))
            {
                return Sitecore.Support.Modules.EmailCampaign.Messages.HtmlMail.FromItem(item);
            }

            if (TextMail.IsCorrectMessageItem(item))
            {
                return TextMail.FromItem(item);
            }

            return null;
        }
    }
}