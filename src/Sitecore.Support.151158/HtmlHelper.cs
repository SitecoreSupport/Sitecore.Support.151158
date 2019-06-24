using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using Sitecore.Configuration;
using Sitecore.Diagnostics;
using Sitecore.ExM.Framework.Diagnostics;
using Sitecore.Modules.EmailCampaign;
using Sitecore.Modules.EmailCampaign.Core;
using Sitecore.Resources.Media;
using Sitecore.Web;
using Sitecore.StringExtensions;

namespace Sitecore.Support.Modules.EmailCampaign.Core
{
    public class HtmlHelper : Sitecore.Modules.EmailCampaign.Core.HtmlHelper
    {

        private delegate FileInMemory Getter(string src, Func<string, FileInMemory> creator);

        private string _baseUrl;

        private readonly Dictionary<string, FileInMemory> _relativeFiles;

        public HtmlHelper(string html) : base(html)
        {
            _baseUrl = string.Empty;
            _relativeFiles = new Dictionary<string, FileInMemory>();
        }

        public Dictionary<string, FileInMemory> RelativeFiles
        {
            get { return _relativeFiles; }
        }

        public void InsertStyleSheets()
        {
            var matches = Regex.Matches(Html, @"\<link[^\>]+\>", RegexOptions.IgnoreCase);

            for (int i = matches.Count - 1; i > -1; i--)
            {
                string tag = matches[i].ToString();
                if (Regex.IsMatch(tag, "rel\\s*=\\s*\"stylesheet\""))
                {
                    var groups = Regex.Match(tag, "href\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase).Groups;
                    if (groups.Count == 2)
                    {
                        string src = groups[1].ToString();
                        var cssFile = GetFile(src, x => new CssFileInMemory(x)) as CssFileInMemory;

                        if (cssFile != null)
                        {
                            string newTag = string.Concat("<style type=\"text/css\">", cssFile.StringContent.ToString(), "</style>");
                            Html = Html.Remove(matches[i].Index, tag.Length).Insert(matches[i].Index, newTag);
                        }
                    }
                }
            }
        }

        private FileInMemory GetFile(string src, Func<string, FileInMemory> createFunc)
        {
            Assert.ArgumentNotNullOrEmpty(src, "src");

            src = HttpUtility.HtmlDecode(src).Trim(' ').TrimStart('/');

            FileInMemory file = null;
            string sourcePath = src;

            try
            {
                if (sourcePath.StartsWith(_baseUrl, true))
                {
                    sourcePath = sourcePath.Remove(0, _baseUrl.Length);
                }

                // whether src is a link to another server
                if (sourcePath.StartsWith("http://") || sourcePath.StartsWith("https://"))
                {
                    file = GatherFile(sourcePath, createFunc, GetExternalFile);
                }
                else

                    // whether src is an ashx request
                if (sourcePath.IndexOf(Settings.Media.MediaLinkPrefix, StringComparison.OrdinalIgnoreCase) > -1 || sourcePath.IndexOf(Settings.Media.DefaultMediaPrefix, StringComparison.OrdinalIgnoreCase) > -1)
                {
                    file = GatherFile(sourcePath, createFunc, GetAshxFileContent);
                }
                else
                {
                    // removing unneeded query parameters
                    int indx = sourcePath.IndexOf('?');
                    if (indx > -1)
                    {
                        sourcePath = sourcePath.Remove(indx);
                    }

                    file = GatherFile(sourcePath, createFunc, GetInternalFile);
                }
            }
            catch (Exception e)
            {
                Logger.Instance.LogError(e);
            }

            if (file != null)
            {
                file.Source = src;
            }

            return file;
        }

        private FileInMemory GatherFile(string sourcePath, Func<string, FileInMemory> createFunc, Getter getFunc)
        {
            var file = FileCache.GetFile(sourcePath.ToLower().GetHashCode());
            if (file != null)
            {
                return file.Clone() as FileInMemory; // new FileInMemory(sourcePath) { Content = file.Content, MimeType = file.MimeType, Name = file.Name };
            }

            file = getFunc(sourcePath, createFunc);
            if (file != null)
            {
                FileCache.SetFile(file.GetHashCode(), file);
            }

            return file;
        }

        private FileInMemory GetExternalFile(string sourcePath, Func<string, FileInMemory> createFunc)
        {
            var content = DownloadExternalContent(sourcePath);
            if (content == null)
            {
                return null;
            }

            var file = createFunc(sourcePath);
            file.SetContent(content);

            return file;
        }

        private FileInMemory GetInternalFile(string sourcePath, Func<string, FileInMemory> createFunc)
        {
            string filePath = HttpRuntime.AppDomainAppPath + sourcePath.Replace('/', '\\');

            if (!File.Exists(filePath))
            {
                return null;
            }

            byte[] byteContent;
            using (var fin = new FileStream(filePath, FileMode.Open))
            {
                byteContent = new byte[(int)fin.Length];
                fin.Read(byteContent, 0, (int)fin.Length);
            }

            var file = createFunc(sourcePath);
            file.SetContent(byteContent);

            return file;
        }

        private FileInMemory GetAshxFileContent(string sourcePath, Func<string, FileInMemory> createFunc)
        {
            var file = createFunc(sourcePath);

            string fullPath = _baseUrl.TrimEnd('/') + '/' + sourcePath.TrimStart('/');

            int start = sourcePath.IndexOf('?');

            var request = new EcmMediaRequest(new HttpRequest(file.Name, WebUtil.GetLocalPath(fullPath), (start > 0) ? sourcePath.Substring(start + 1) : string.Empty));
            var media = MediaManager.GetMedia(request.MediaUri);
            if (media == null)
            {
                return null;
            }

            using (var mediaStream = media.GetStream(request.Options))
            {
                file.Name = mediaStream.FileName;
                file.MimeType = mediaStream.MimeType;

                var stream = mediaStream.Stream;

                var byteContent = new byte[stream.Length];
                mediaStream.Stream.Read(byteContent, 0, (int)stream.Length);
                file.SetContent(byteContent);

                return file;
            }
        }

        public void CollectRelativeFiles(string baseUrl)
        {
            _baseUrl = baseUrl;

            var patterns = new List<string>
            {
                "<img[^>]*\\ssrc\\s*=\\s*\"([^\"]+)\"", // <img src="..." />
                "background(?:-image)?:.*url\\(\'([^\"]+)\'"
            };

            foreach (string pattern in patterns)
            {
                CollectHtmlRelativeFiles(pattern);
            }
        }

        private void CollectHtmlRelativeFiles(string pattern)
        {
            var startTime = DateTime.UtcNow;

            var matches = Regex.Matches(Html, pattern, RegexOptions.IgnoreCase);

            Util.TraceTimeDiff("Regex search '" + pattern + "'", startTime);
            startTime = DateTime.UtcNow;

            var html = new StringBuilder(Html);

            for (int i = matches.Count - 1; i > -1; i--)
            {
                var groups = matches[i].Groups;
                if (groups.Count != 2)
                {
                    continue;
                }

                string src = groups[1].ToString();

                if (src.Contains(GlobalSettings.OpenHandlerPath))
                {
                    continue;
                }

                string correctedSrc = GetSrc(src);

                if (_relativeFiles.ContainsKey(correctedSrc))
                {
                    continue;
                }

                var file = GetFile(correctedSrc, x => new FileInMemory(x));
                if (file == null)
                {
                    continue;
                }

                _relativeFiles.Add(correctedSrc, file);

                if (correctedSrc[0] != '/')
                {
                    continue;
                }

                file.Source = _baseUrl + "/" + file.Source;

                html = html.Replace(src, file.GetHashCode().ToString(CultureInfo.InvariantCulture));
            }

            Html = html.ToString();

            Util.TraceTimeDiff("Pattern files collecting '" + pattern + "'", startTime);
        }

        internal string GetSrc(string src)
        {
            var srcReplaced = new StringBuilder(src);
            srcReplaced = srcReplaced.Replace(Settings.Media.DefaultMediaPrefix, string.Format("/{0}", Settings.Media.DefaultMediaPrefix));
            srcReplaced = srcReplaced.Replace(Settings.Media.MediaLinkPrefix, string.Format("/{0}", Settings.Media.MediaLinkPrefix));
            srcReplaced = srcReplaced.Replace(" ", "%20");
            srcReplaced = srcReplaced.Replace("%7E", "~");
            srcReplaced = srcReplaced.Replace("%2D", "-");

            string correctedSrc = srcReplaced.ToString();
            return correctedSrc;
        }
    }
}