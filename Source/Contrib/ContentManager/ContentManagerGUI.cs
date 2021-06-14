// COPYRIGHT 2014 by the Open Rails project.
//
// This file is part of Open Rails.
//
// Open Rails is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// Open Rails is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with Open Rails.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Orts.Settings;
using static Orts.Common.Native.NativeMethods;

namespace Orts.ContentManager
{
    public partial class ContentManagerGUI : Form
    {
        private readonly UserSettings settings;
        private readonly ContentManager contentManager;

        private static readonly Regex contentBold = new Regex("(?:^|\t)([\\w ]+:)\t", RegexOptions.Multiline);
        private static readonly Regex contentLink = new Regex("\u0001(.*?)\u0002(.*?)\u0001");

        private Content pendingSelection;
        private List<string> pendingSelections = new List<string>();

        private CancellationTokenSource ctsSearching;
        private CancellationTokenSource ctsExpanding;
        private readonly ContentType[] contentTypes = (ContentType[])Enum.GetValues(typeof(ContentType));

        private ConcurrentBag<SearchResult> searchResultsList = new ConcurrentBag<SearchResult>();
        private ConcurrentDictionary<string, string> searchResultDuplicates;
        private static CharFormat2 rtfLink = new CharFormat2
        {
            Size = Marshal.SizeOf(typeof(CharFormat2)),
            Mask = CfmLink,
            Effects = CfmLink,
        };

        private const int WmUser = 0x0400;
        private const int EmSetCharFormat = WmUser + 68;
        private const int ScfSelection = 0x0001;
        private const int CfmLink = 0x00000020;

        public ContentManagerGUI()
        {
            InitializeComponent();

            settings = new UserSettings(Array.Empty<string>());
            contentManager = new ContentManager(settings.FolderSettings);

            // Start off the tree with the Content Manager itself at the root and expand to show packages.
            treeViewContent.Nodes.Add(CreateContentNode(contentManager));
            treeViewContent.Nodes[0].Expand();

            var width = richTextBoxContent.Font.Height;
            richTextBoxContent.SelectionTabs = new[] { width * 5, width * 15, width * 25, width * 35 };

        }

        private Task<IEnumerable<TreeNode>> ExpandTreeView(Content content, CancellationToken token)
        {

            // Get all the different possible types of content from this content item.
            var childNodes = contentTypes.SelectMany(ct => content.Get(ct).Select(c => CreateContentNode(c)));

            if (token.IsCancellationRequested)
            {
                return Task.FromCanceled<IEnumerable<TreeNode>>(token);
            }
            var linkChildren = contentLink.Matches(ContentInfo.GetText(content)).Cast<Match>().Select(linkMatch => CreateContentNode(content, linkMatch.Groups[1].Value, (ContentType)Enum.Parse(typeof(ContentType), linkMatch.Groups[2].Value)));
            Debug.Assert(!childNodes.Any() || !linkChildren.Any(), "Content item should not return items from Get(ContentType) and Get(string, ContentType)");
            childNodes = childNodes.Concat(linkChildren);

            if (token.IsCancellationRequested)
            {
                return Task.FromCanceled<IEnumerable<TreeNode>>(token);
            }
            return Task.FromResult(childNodes);
        }

        private async void TreeViewContent_BeforeExpand(object sender, TreeViewCancelEventArgs e)
        {
            // Are we about to expand a not-yet-loaded node? This is identified by a single child with no text or tag.
            if (e.Node.Tag != null && e.Node.Tag is Content && e.Node.Nodes.Count == 1 && string.IsNullOrEmpty(e.Node.Nodes[0].Text) && e.Node.Nodes[0].Tag == null)
            {
                // Make use of the single child to show a loading message.
                e.Node.Nodes[0].Text = "Loading...";
                lock (e.Node)
                {
                    if (ctsExpanding != null && !ctsExpanding.IsCancellationRequested)
                        ctsExpanding.Cancel();
                    ctsExpanding = ResetCancellationTokenSource(ctsExpanding);
                }
                try
                {

                    Content content = e.Node.Tag as Content;
                    var nodes = await Task.Run(() => ExpandTreeView(content, ctsExpanding.Token)).ConfigureAwait(false);

                    //// Collapse node if we're going to end up with no child nodes.
                    if (!nodes.Any())
                    {
                        e.Node.Collapse();
                    }

                    // Remove the loading node.
                    e.Node.Nodes.RemoveAt(0);
                    e.Node.Nodes.AddRange(nodes.ToArray());
                }
                catch (TaskCanceledException)
                {
                    e.Node.Nodes[0].Text = string.Empty;
                    e.Node.Collapse();
                }
                catch (Exception ex)
                {
                    e.Node.Nodes.Add(new TreeNode(ex.Message));
                }
            }
            CheckForPendingActions(e.Node);
        }

        private void CheckForPendingActions(TreeNode startNode)
        {
            if (pendingSelection != null)
            {
                var pendingSelectionNode = startNode.Nodes.OfType<TreeNode>().FirstOrDefault(node => (Content)node.Tag == pendingSelection);
                if (pendingSelectionNode != null)
                {
                    treeViewContent.SelectedNode = pendingSelectionNode;
                    treeViewContent.Focus();
                }
                pendingSelection = null;
            }
            if (pendingSelections.Count > 0)
            {
                var pendingSelectionNode = startNode.Nodes.OfType<TreeNode>().FirstOrDefault(node => ((Content)node.Tag).Name == pendingSelections[0]);
                if (pendingSelectionNode != null)
                {
                    pendingSelections.RemoveAt(0);
                    treeViewContent.SelectedNode = pendingSelectionNode;
                    treeViewContent.SelectedNode.Expand();
                }
            }
        }

        private static TreeNode CreateContentNode(Content content, string name, ContentType type)
        {
            var c = content.Get(name, type);
            if (c != null)
                return CreateContentNode(c);
            return new TreeNode($"Missing: {name} ({type})");
        }

        private static TreeNode CreateContentNode(Content c)
        {
            return new TreeNode($"{c.Name} ({c.Type})", new[] { new TreeNode() }) { Tag = c };
        }

        private void TreeViewContent_AfterSelect(object sender, TreeViewEventArgs e)
        {
            richTextBoxContent.Clear();

            if (!(e.Node.Tag is Content))
                return;

            Trace.TraceInformation("Updating richTextBoxContent with content {0}", e.Node.Tag as Content);
            richTextBoxContent.Text = ContentInfo.GetText(e.Node.Tag as Content);
            var boldFont = new Font(richTextBoxContent.Font, FontStyle.Bold);
            var boldMatch = contentBold.Match(richTextBoxContent.Text);
            while (boldMatch.Success)
            {
                richTextBoxContent.Select(boldMatch.Groups[1].Index, boldMatch.Groups[1].Length);
                richTextBoxContent.SelectionFont = boldFont;
                boldMatch = contentBold.Match(richTextBoxContent.Text, boldMatch.Groups[1].Index + boldMatch.Groups[1].Length);
            }
            var linkMatch = contentLink.Match(richTextBoxContent.Text);
            while (linkMatch.Success)
            {
                richTextBoxContent.Select(linkMatch.Index, linkMatch.Length);
                richTextBoxContent.SelectedRtf = $@"{{\rtf{{{linkMatch.Groups[1].Value}{{\v{{\u1.{linkMatch.Groups[1].Value}\u1.{linkMatch.Groups[2].Value}}}\v0}}}}}}";
                richTextBoxContent.Select(linkMatch.Index, linkMatch.Groups[1].Value.Length * 2 + linkMatch.Groups[2].Value.Length + 2);
                SendMessage(richTextBoxContent.Handle, EmSetCharFormat, (IntPtr)ScfSelection, ref rtfLink);
                linkMatch = contentLink.Match(richTextBoxContent.Text);
            }
            richTextBoxContent.Select(0, 0);
            richTextBoxContent.SelectionFont = richTextBoxContent.Font;
        }

        private void RichTextBoxContent_LinkClicked(object sender, LinkClickedEventArgs e)
        {
            var content = treeViewContent.SelectedNode.Tag as Content;
            var link = e.LinkText.Split('\u0001');
            if (content != null && link.Length == 3)
            {
                pendingSelection = content.Get(link[1], (ContentType)Enum.Parse(typeof(ContentType), link[2]));
                if (treeViewContent.SelectedNode.IsExpanded)
                {
                    var pendingSelectionNode = treeViewContent.SelectedNode.Nodes.Cast<TreeNode>().FirstOrDefault(node => (Content)node.Tag == pendingSelection);
                    if (pendingSelectionNode != null)
                    {
                        treeViewContent.SelectedNode = pendingSelectionNode;
                        treeViewContent.Focus();
                    }
                    pendingSelection = null;
                }
                else
                {
                    treeViewContent.SelectedNode.Expand();
                }
            }
        }

        private Task SearchContent(Content content, string path, string searchString, CancellationToken token)
        {
            if (token.IsCancellationRequested)
                return Task.CompletedTask;

            try
            {
                if (string.IsNullOrEmpty(path))
                    path = contentManager.Name;

                if (content.Name.ToLowerInvariant().Contains(searchString))
                {
                    if (content.Parent != null)
                        searchResultsList.Add(new SearchResult(content, path));
                }
                Parallel.ForEach(contentTypes.SelectMany(ct => content.Get(ct)),
                    new ParallelOptions() { CancellationToken = token },
                    async (child) =>
                {
                     await SearchContent(child, path + " / " + child.Name, searchString, token);
                });

                Parallel.ForEach(contentLink.Matches(ContentInfo.GetText(content)).Cast<Match>().Select(linkMatch => content.Get(linkMatch.Groups[1].Value,
                    (ContentType)Enum.Parse(typeof(ContentType), linkMatch.Groups[2].Value))).Where(linkContent => linkContent != null),
                    new ParallelOptions() { CancellationToken = token },
                    async (child) =>
                    {
                        if (!searchResultDuplicates.ContainsKey(path + " -> " + child.Name))
                        {
                            searchResultDuplicates.TryAdd(path, content.Name);
                            await SearchContent(child, path + " -> " + child.Name, searchString, token);
                        }
                    });

                if (!token.IsCancellationRequested)
                    return UpdateSearchResults(token);
            }
            catch (OperationCanceledException)
            {
            }
            return Task.CompletedTask;
        }

        private async void SearchBox_TextChanged(object sender, EventArgs e)
        {
            lock (searchResultsList)
            {
                if (ctsSearching != null && !ctsSearching.IsCancellationRequested)
                    ctsSearching.Cancel();
                ctsSearching = ResetCancellationTokenSource(ctsSearching);
            }

            searchResultsList = new ConcurrentBag<SearchResult>();
            searchResultDuplicates = new ConcurrentDictionary<string, string>();
            searchResults.Items.Clear();
            searchResults.Visible = searchBox.Text.Length > 0;
            if (!searchResults.Visible)
                return;

            searchResults.Items.Add(string.Empty);

            try
            {
                await Task.Run(() => SearchContent(contentManager, string.Empty, searchBox.Text, ctsSearching.Token));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                UpdateSearchResults(true);
            }
            return;
        }

        private void UpdateSearchResults(bool done)
        {
            while (searchResultsList.TryTake(out SearchResult result))
            {
                searchResults.Items.Add(result);
            }
            if (searchResults.Items.Count > 0)
                searchResults.Items[0] = $"[{(done ? "Done" : "Searching")}] {searchResults.Items.Count - 1} results found";
        }

        private Task UpdateSearchResults(CancellationToken token)
        {
            try
            {
                Invoke((MethodInvoker)delegate { UpdateSearchResults(false); });
            }
            catch (ObjectDisposedException) //when Form Closing, object may already be disposing while SearchTask cancelling
            { }
            return Task.CompletedTask;
        }

        private void SearchResults_DoubleClick(object sender, EventArgs e)
        {
            if (!(searchResults.SelectedItem is SearchResult result))
                return;

            pendingSelections.Clear();
            pendingSelections.AddRange(result.Path);
            treeViewContent.Focus();
            treeViewContent.CollapseAll();
            treeViewContent.SelectedNode = treeViewContent.Nodes[0];
            treeViewContent.SelectedNode.Expand();
        }

        private static CancellationTokenSource ResetCancellationTokenSource(System.Threading.CancellationTokenSource cts)
        {
            if (cts != null)
            {
                cts.Dispose();
            }
            // Create a new cancellation token source so that can cancel all the tokens again 
            return new CancellationTokenSource();
        }

        private void ContentManagerGUI_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (null != ctsSearching && !ctsSearching.IsCancellationRequested)
                ctsSearching.Cancel();
            if (null != ctsExpanding && !ctsExpanding.IsCancellationRequested)
                ctsExpanding.Cancel();
        }
    }

    public class SearchResult
    {
        public string Name;
        public string[] Path;
        private static string[] separators = { " / ", " -> " };
        public SearchResult(Content content, string path)
        {
            var placeEnd = Math.Max(path.LastIndexOf(" / "), path.LastIndexOf(" -> "));
            var place = path.Substring(0, placeEnd);
            Name = $"{content.Name} ({content.Type}) in {place}";
            Path = path.Split(separators, StringSplitOptions.None).Skip(1).ToArray();
        }

        public override string ToString()
        {
            return Name;
        }
    }

}
