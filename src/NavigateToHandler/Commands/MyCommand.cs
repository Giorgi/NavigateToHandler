﻿using System.Collections.Generic;
using System.Linq;
using HandlerLocator;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;

namespace NavigateToHandler
{
    [Command(PackageIds.MyCommand)]
    internal sealed class MyCommand : BaseCommand<MyCommand>
    {
        private const string PANE_TITLE = "Navigate to Handler";
        private OutputWindowPane _pane;

        // Oy vey
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            // Initialize our pane
            _pane ??= await VS.Windows.CreateOutputWindowPaneAsync(PANE_TITLE, lazyCreate: true);

            VisualStudioWorkspace workspace = await VS.GetMefServiceAsync<VisualStudioWorkspace>();
            if (workspace is null)
                return;

            DocumentView documentView = await VS.Documents.GetActiveDocumentViewAsync();
            if (documentView is null)
                return;

            DocumentId documentId = workspace.CurrentSolution.GetDocumentIdsWithFilePath(documentView.FilePath).FirstOrDefault();
            if (documentId is null)
            {
                return;
            }

            // Get Roslyn document
            Document roslynDocument = workspace.CurrentSolution.GetDocument(documentId);

            // Get the position under the cursor
            int position = documentView.TextView.Selection.ActivePoint.Position.Position;

            FindHandlerLocator locator = new(workspace.CurrentSolution, roslynDocument, position);
            IEnumerable<IdentifiedHandler> allHandlers = await locator.FindAllHandlers();
            if (allHandlers is null || !allHandlers.Any())
            {
                await DisplayNoLoveAsync();
                return;
            }

            if (allHandlers.Count() == 1)
            {
                await DisplayHandlerAsync(allHandlers.First());
                return;
            }

            await DisplayHandlersInOutputPaneAsync(allHandlers);
        }

        private async Task DisplayNoLoveAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            await _pane.ClearAsync();
            await _pane.WriteLineAsync($"No handlers found for the type under the cursor.");
            await _pane.ActivateAsync();
            BringOutputWindowToFocus();
        }

        private async Task DisplayHandlersInOutputPaneAsync(IEnumerable<IdentifiedHandler> allHandlers)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            string message = $"Found {allHandlers.Count()} public methods that consume '{allHandlers.First().TypeToFind}':";
            string underlines = new('-', message.Length);

            await _pane.ClearAsync();

            using System.IO.TextWriter writer = await _pane.CreateOutputPaneTextWriterAsync();

            await writer.WriteLineAsync(message);
            await writer.WriteLineAsync(underlines);

            foreach (IdentifiedHandler handler in allHandlers.OrderBy(h => h.SourceFile).ThenBy(h => h.TypeName).ThenBy(h => h.PublicMethod))
            {
                await writer.WriteLineAsync($"{handler.DisplaySourceFile}:{handler.Fill}{handler.TypeName}.{handler.PublicMethod}() as {handler.AsArgument}");
            }

            await writer.WriteLineAsync(underlines);
            await writer.WriteLineAsync($"Double-click the relevant line to open." + Environment.NewLine);

            await _pane.ActivateAsync();
            BringOutputWindowToFocus();
        }

        private void BringOutputWindowToFocus()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            IVsUIShell uiShell = VS.GetRequiredService<SVsUIShell, IVsUIShell>();
            if (uiShell != null)
            {
                // CLSID of the output window
                Guid clsidOutputWindow = new Guid(ToolWindowGuids.Outputwindow);
                IVsWindowFrame windowFrame = null;
                uiShell.FindToolWindow((uint)__VSFINDTOOLWIN.FTW_fForceCreate, ref clsidOutputWindow, out windowFrame);

                if (windowFrame != null)
                {
                    windowFrame.Show();
                }
            }
        }

        private async Task DisplayHandlerAsync(IdentifiedHandler identifiedHandler)
        {
            await _pane.ClearAsync();
            await _pane.WriteLineAsync($"Match found: {identifiedHandler.TypeName}.{identifiedHandler.PublicMethod}(), line: {identifiedHandler.LineNumber}, column: {identifiedHandler.Column}");

            DocumentView openedView = await VS.Documents.OpenAsync(identifiedHandler.SourceFile);
            openedView.TextView.Caret.MoveTo(new SnapshotPoint(openedView.TextBuffer.CurrentSnapshot, identifiedHandler.CaretPosition));
            openedView.TextView.Caret.EnsureVisible();
        }
    }
}
