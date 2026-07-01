using ContextMessenger.App.Wpf.Patching;
using ContextMessenger.App.Wpf.ViewModels;
using ContextMessenger.Protocol.Dispatch;
using ContextMessenger.Protocol.Review;

namespace ContextMessenger.App.Wpf.Tests;

public sealed class PatchReviewViewModelTests
{
    private static HeldPatchInteraction Held(
        string status = PatchTransactionStatuses.AwaitingAcceptance,
        PatchInteractionPhase phase = PatchInteractionPhase.Reviewing) => new()
    {
        RootName = "Repo",
        TargetName = "ChatGPT",
        PatchId = "p-1",
        Revision = 1,
        TransactionStatus = status,
        Phase = phase,
        HeldResponseText = "BEGIN_RESPONSE\n{}\nEND_RESPONSE",
    };

    // The review tab is the current tab in these scenarios, so the VM is marked active. The
    // separate Inactive_review_tab test covers the not-current-tab gating.
    private static PatchReviewViewModel ViewModel(out RecordingActions actions)
    {
        actions = new RecordingActions();
        return new PatchReviewViewModel(actions) { IsActive = true };
    }

    [Fact]
    public void No_interaction_disables_all_commands()
    {
        var vm = ViewModel(out _);

        Assert.False(vm.HasInteraction);
        Assert.False(vm.SendCommand.CanExecute(null));
        Assert.False(vm.AcceptCommand.CanExecute(null));
        Assert.False(vm.RevertCommand.CanExecute(null));
        Assert.False(vm.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public void Inactive_review_tab_disables_all_commands_even_with_a_held_patch()
    {
        var vm = ViewModel(out _);
        vm.Update(Held(PatchTransactionStatuses.AwaitingAcceptance, PatchInteractionPhase.Reviewing));
        Assert.True(vm.AcceptCommand.CanExecute(null)); // enabled while the review tab is current

        vm.IsActive = false; // reviewer switched to a log tab

        Assert.False(vm.SendCommand.CanExecute(null));
        Assert.False(vm.AcceptCommand.CanExecute(null));
        Assert.False(vm.RevertCommand.CanExecute(null));
        Assert.False(vm.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public void Validated_reviewing_patch_enables_accept_revert_refresh_but_not_send()
    {
        var vm = ViewModel(out _);
        vm.Update(Held(PatchTransactionStatuses.AwaitingAcceptance, PatchInteractionPhase.Reviewing));

        Assert.False(vm.SendCommand.CanExecute(null)); // validated, nothing to say
        Assert.True(vm.AcceptCommand.CanExecute(null));
        Assert.True(vm.RevertCommand.CanExecute(null));
        Assert.True(vm.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public void A_comment_enables_send_even_on_a_validated_patch()
    {
        var vm = ViewModel(out _);
        vm.Update(Held(PatchTransactionStatuses.AwaitingAcceptance, PatchInteractionPhase.Reviewing));
        Assert.False(vm.SendCommand.CanExecute(null));

        vm.AddComment(3, "is this intended?");

        Assert.True(vm.HasComments);
        Assert.True(vm.SendCommand.CanExecute(null));
    }

    [Fact]
    public void Needs_revision_patch_does_not_enable_accept()
    {
        var vm = ViewModel(out _);
        vm.Update(Held(PatchTransactionStatuses.NeedsRevision, PatchInteractionPhase.Reviewing));

        Assert.True(vm.SendCommand.CanExecute(null));
        Assert.False(vm.AcceptCommand.CanExecute(null)); // not validated
        Assert.True(vm.RevertCommand.CanExecute(null));
    }

    [Fact]
    public void Accept_enabled_regardless_of_phase_for_validated_patch()
    {
        var vm = ViewModel(out _);
        vm.Update(Held(PatchTransactionStatuses.AwaitingAcceptance, PatchInteractionPhase.AwaitingModelReply));

        Assert.False(vm.SendCommand.CanExecute(null)); // floor is the model's
        Assert.True(vm.AcceptCommand.CanExecute(null)); // accept ignores phase
    }

    [Fact]
    public void Awaiting_model_reply_disables_send_but_keeps_revert_and_refresh()
    {
        var vm = ViewModel(out _);
        vm.Update(Held(phase: PatchInteractionPhase.AwaitingModelReply));

        Assert.False(vm.SendCommand.CanExecute(null)); // floor is the model's
        Assert.True(vm.RevertCommand.CanExecute(null));
        Assert.True(vm.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public void Update_to_null_disables_everything()
    {
        var vm = ViewModel(out _);
        vm.Update(Held());
        Assert.True(vm.RevertCommand.CanExecute(null));

        vm.Update(null);

        Assert.False(vm.HasInteraction);
        Assert.False(vm.SendCommand.CanExecute(null));
        Assert.False(vm.RevertCommand.CanExecute(null));
        Assert.False(vm.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public async Task Accept_Revert_and_Refresh_invoke_actions()
    {
        var vm = ViewModel(out var actions);
        vm.Update(Held(PatchTransactionStatuses.AwaitingAcceptance));

        await vm.AcceptCommand.ExecuteAsync(null);
        await vm.RevertCommand.ExecuteAsync(null);
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(1, actions.AcceptCount);
        Assert.Equal(1, actions.RevertCount);
        Assert.Equal(1, actions.RefreshCount);
    }

    [Fact]
    public void Projects_interaction_fields_and_summary()
    {
        var vm = ViewModel(out _);
        Assert.Equal("No patch under review.", vm.Summary);

        vm.Update(Held(PatchTransactionStatuses.AwaitingAcceptance));

        Assert.True(vm.HasInteraction);
        Assert.Equal("p-1", vm.PatchId);
        Assert.Equal(1, vm.Revision);
        Assert.Equal(PatchTransactionStatuses.AwaitingAcceptance, vm.TransactionStatus);
        Assert.Equal("Reviewing", vm.Phase);
        Assert.Contains("p-1", vm.Summary);

        vm.Update(null);
        Assert.Equal("No patch under review.", vm.Summary);
    }

    [Fact]
    public void Update_populates_files_and_descriptive_fields_from_snapshot()
    {
        var vm = ViewModel(out var actions);
        actions.Snapshot = new PatchReviewSnapshot
        {
            Title = "My patch",
            Description = "Does things",
            CommitMessage = "feat: things",
            PatchId = "p-1",
            Revision = 2,
            Status = PatchTransactionStatuses.AwaitingAcceptance,
            Files =
            [
                new PatchReviewFile { Path = "src/A.cs", Operation = "replace" },
                new PatchReviewFile { Path = "src/B.cs", Operation = "create" },
            ],
        };

        vm.Update(Held());

        var folder = Assert.Single(vm.RootNodes);
        Assert.True(folder.IsFolder);
        Assert.Equal("src", folder.Name);
        Assert.Equal(2, folder.Children.Count);
        Assert.Equal("My patch", vm.Title);
        Assert.Equal("Does things", vm.Description);
        Assert.Equal("feat: things", vm.CommitMessage);
        Assert.Equal("src/A.cs", vm.SelectedFile?.Path); // first file auto-selected
    }

    [Fact]
    public void Selecting_a_file_loads_and_parses_its_diff()
    {
        var vm = ViewModel(out var actions);
        actions.Snapshot = new PatchReviewSnapshot
        {
            Files = [new PatchReviewFile { Path = "src/A.cs", Operation = "replace" }],
        };
        actions.FileDiff = "@@ -1 +1 @@\n-old\n+new\n";

        vm.Update(Held());

        Assert.Equal("src/A.cs", actions.LastDiffPath);
        Assert.Contains(vm.SelectedFileDiff, l => l.Kind == DiffLineKind.Added && l.Text == "new");
        Assert.Contains(vm.SelectedFileDiff, l => l.Kind == DiffLineKind.Removed && l.Text == "old");
        Assert.Contains(vm.SelectedFileDiff, l => l.Kind == DiffLineKind.Added && l.NewLineNumber == 1);
        Assert.Contains(vm.SelectedFileDiff, l => l.Kind == DiffLineKind.Removed && l.OldLineNumber == 1 && l.NewLineNumber is null);
        Assert.DoesNotContain(vm.SelectedFileDiff, l => l.Kind == DiffLineKind.Hunk); // header/hunk filtered out
    }

    [Fact]
    public void Closing_review_clears_tree_and_diff()
    {
        var vm = ViewModel(out var actions);
        actions.Snapshot = new PatchReviewSnapshot
        {
            Files = [new PatchReviewFile { Path = "x", Operation = "replace" }],
        };
        actions.FileDiff = "@@ -1 +1 @@\n+new\n";
        vm.Update(Held());
        Assert.NotEmpty(vm.RootNodes);

        vm.Update(null);

        Assert.Empty(vm.RootNodes);
        Assert.Null(vm.SelectedFile);
        Assert.Empty(vm.SelectedFileDiff);
    }

    [Fact]
    public void Accept_is_disabled_when_the_patch_has_no_changed_files()
    {
        var vm = ViewModel(out var actions);
        actions.Snapshot = PatchReviewSnapshot.Empty; // a fix reverted the tree to clean
        vm.Update(Held(PatchTransactionStatuses.AwaitingAcceptance));

        Assert.False(vm.AcceptCommand.CanExecute(null)); // nothing to accept
        Assert.True(vm.RevertCommand.CanExecute(null));   // can still cancel the review
        Assert.False(vm.SendCommand.CanExecute(null));    // validated + no comments -> nothing to send
    }

    [Fact]
    public void History_rows_put_held_response_on_the_last_row_only()
    {
        var vm = ViewModel(out _);
        var interaction = Held() with
        {
            HeldResponseText = "BEGIN_RESPONSE held END_RESPONSE",
            History =
            [
                new PatchInteractionEntry { Direction = PatchInteractionDirection.Inbound, Summary = "propose -> needs_revision", Revision = 1 },
                new PatchInteractionEntry { Direction = PatchInteractionDirection.Inbound, Summary = "amend -> awaiting_acceptance", Revision = 2 },
            ],
        };

        vm.Update(interaction);

        Assert.Equal(2, vm.HistoryRows.Count);
        Assert.False(vm.HistoryRows[0].HasHeldResponse);
        Assert.True(vm.HistoryRows[1].HasHeldResponse);
        Assert.Contains("held", vm.HistoryRows[1].HeldResponse);
    }

    [Theory]
    [InlineData(PatchTransactionStatuses.AwaitingAcceptance, true, false)]
    [InlineData(PatchTransactionStatuses.NeedsRevision, false, true)]
    public void Tab_status_flags_reflect_transaction_status(string status, bool validated, bool invalid)
    {
        var vm = ViewModel(out _);

        vm.Update(Held(status));

        Assert.Equal(validated, vm.IsValidated);
        Assert.Equal(invalid, vm.IsInvalid);
    }

    [Fact]
    public void TabTitle_uses_status_when_under_review()
    {
        var vm = ViewModel(out _);
        Assert.Equal("Review", vm.TabTitle);

        vm.Update(Held(PatchTransactionStatuses.NeedsRevision));

        Assert.Equal("Review – needs_revision", vm.TabTitle);
    }

    [Fact]
    public void Build_errors_populate_rows_and_auto_select_the_errors_tab()
    {
        var vm = ViewModel(out var actions);
        actions.Snapshot = new PatchReviewSnapshot
        {
            Files = [new PatchReviewFile { Path = "src/App/Main.cs", Operation = "replace" }],
        };
        var interaction = Held(PatchTransactionStatuses.NeedsRevision) with
        {
            BuildErrors = [new PatchBuildError { Code = "CS1002", Path = "C:/repo/src/App/Main.cs", Line = 12, Column = 4, Message = "; expected" }],
        };

        vm.Update(interaction);

        Assert.True(vm.HasBuildErrors);
        var row = Assert.Single(vm.BuildErrors);
        Assert.Equal("CS1002", row.Code);
        Assert.Contains("12", row.Location);
        Assert.Equal(1, vm.DetailTabIndex); // Errors tab (index 1) auto-selected
    }

    [Fact]
    public void Build_warnings_populate_rows_but_do_not_auto_select_or_invalidate()
    {
        var vm = ViewModel(out var actions);
        actions.Snapshot = new PatchReviewSnapshot
        {
            Files = [new PatchReviewFile { Path = "src/App/Main.cs", Operation = "replace" }],
        };
        var interaction = Held(PatchTransactionStatuses.AwaitingAcceptance) with
        {
            BuildWarnings = [new PatchBuildWarning { Code = "CS0168", Path = "C:/repo/src/App/Main.cs", Line = 12, Column = 4, Message = "unused" }],
            BuildSummary = new PatchStageSummary { Status = "passed", Policy = "solution", DurationMs = 123 },
            TestSummary = new PatchStageSummary { Status = "passed", TotalTests = 2, ExecutedTests = 2, PassedTests = 2, FailedTests = 0, SkippedTests = 0 },
        };

        vm.Update(interaction);

        Assert.True(vm.HasBuildWarnings);
        Assert.False(vm.IsInvalid);
        Assert.True(vm.AcceptCommand.CanExecute(null));
        Assert.Equal(0, vm.DetailTabIndex);
        var row = Assert.Single(vm.BuildWarnings);
        Assert.Equal("CS0168", row.Code);
        Assert.Contains("12", row.Location);
        Assert.Contains("Build: passed", vm.BuildSummary);
        Assert.Contains("Tests: passed", vm.TestSummary);
    }

    [Fact]
    public void OpenWarning_selects_the_associated_changed_file()
    {
        var vm = ViewModel(out var actions);
        actions.Snapshot = new PatchReviewSnapshot
        {
            Files =
            [
                new PatchReviewFile { Path = "src/App/Main.cs", Operation = "replace" },
                new PatchReviewFile { Path = "src/App/Other.cs", Operation = "replace" },
            ],
        };
        var interaction = Held(PatchTransactionStatuses.AwaitingAcceptance) with
        {
            BuildWarnings = [new PatchBuildWarning { Code = "CS0168", Path = "C:/repo/src/App/Other.cs", Line = 7, Message = "unused" }],
        };
        vm.Update(interaction);

        vm.OpenWarningCommand.Execute(vm.BuildWarnings[0]);

        Assert.Equal("src/App/Other.cs", vm.SelectedFile?.Path);
        Assert.Equal(7, vm.SelectedFileLine);
    }

    [Fact]
    public void No_build_errors_keeps_history_tab_selected()
    {
        var vm = ViewModel(out var actions);
        actions.Snapshot = new PatchReviewSnapshot
        {
            Files = [new PatchReviewFile { Path = "src/A.cs", Operation = "replace" }],
        };

        vm.Update(Held(PatchTransactionStatuses.AwaitingAcceptance));

        Assert.False(vm.HasBuildErrors);
        Assert.Equal(0, vm.DetailTabIndex);
    }

    [Fact]
    public void OpenError_selects_the_associated_changed_file()
    {
        var vm = ViewModel(out var actions);
        actions.Snapshot = new PatchReviewSnapshot
        {
            Files =
            [
                new PatchReviewFile { Path = "src/App/Main.cs", Operation = "replace" },
                new PatchReviewFile { Path = "src/App/Other.cs", Operation = "create" },
            ],
        };
        actions.FileDiff = "@@ -1 +1 @@\n+x\n";
        var interaction = Held(PatchTransactionStatuses.NeedsRevision) with
        {
            BuildErrors = [new PatchBuildError { Code = "CS1002", Path = "C:/repo/src/App/Other.cs", Line = 7, Message = "bad" }],
        };
        vm.Update(interaction);

        vm.OpenErrorCommand.Execute(vm.BuildErrors[0]);

        Assert.Equal("src/App/Other.cs", vm.SelectedFile?.Path);
        Assert.Equal(7, vm.SelectedFileLine); // diff caret jumps to the error line
    }

    [Fact]
    public void Same_patch_refresh_preserves_selected_file_when_it_still_exists()
    {
        var vm = ViewModel(out var actions);
        actions.Snapshot = new PatchReviewSnapshot
        {
            Files =
            [
                new PatchReviewFile { Path = "src/App/Main.cs", Operation = "replace" },
                new PatchReviewFile { Path = "src/App/Other.cs", Operation = "replace" },
            ],
        };
        vm.Update(Held(PatchTransactionStatuses.NeedsRevision));
        vm.SelectNode(FindFile(vm.RootNodes, "src/App/Other.cs"));

        actions.Snapshot = actions.Snapshot with
        {
            Revision = 2,
            Files =
            [
                new PatchReviewFile { Path = "src/App/Main.cs", Operation = "replace" },
                new PatchReviewFile { Path = "src/App/Other.cs", Operation = "replace" },
            ],
        };
        vm.Update(Held(PatchTransactionStatuses.NeedsRevision) with { Revision = 2 });

        Assert.Equal("src/App/Other.cs", vm.SelectedFile?.Path);
    }

    [Fact]
    public void Test_failures_populate_rows_and_select_the_tests_tab()
    {
        var vm = ViewModel(out var actions);
        actions.Snapshot = new PatchReviewSnapshot
        {
            Files = [new PatchReviewFile { Path = "src/MyTests.cs", Operation = "create" }],
        };
        var interaction = Held(PatchTransactionStatuses.NeedsRevision) with
        {
            TestFailures = [new PatchTestFailure { Code = "Ns.MyTests.Fails", Path = "src/MyTests.cs", Line = 20, Message = "boom" }],
        };

        vm.Update(interaction);

        Assert.True(vm.HasTestFailures);
        var row = Assert.Single(vm.TestFailures);
        Assert.Equal("Ns.MyTests.Fails", row.Name);
        Assert.True(row.CanJump); // the test file is part of the patch
        Assert.Equal(3, vm.DetailTabIndex); // Tests tab auto-selected (no build errors)
    }

    [Fact]
    public void Policy_failure_populates_errors_tab_without_file_location()
    {
        var vm = ViewModel(out var actions);
        actions.Snapshot = new PatchReviewSnapshot
        {
            Files = [new PatchReviewFile { Path = "src/App.cs", Operation = "replace" }],
        };
        var interaction = Held(PatchTransactionStatuses.NeedsRevision) with
        {
            BuildErrors =
            [
                new PatchBuildError
                {
                    Code = "invalid_patch_policy",
                    Message = "build stage failed: Unsupported build policy 'bogus'.",
                },
            ],
        };

        vm.Update(interaction);

        Assert.True(vm.HasBuildErrors);
        var row = Assert.Single(vm.BuildErrors);
        Assert.Equal("invalid_patch_policy", row.Code);
        Assert.Equal("", row.Path);
        Assert.Equal("", row.Location);
        Assert.Equal("build stage failed: Unsupported build policy 'bogus'.", row.Message);
        Assert.Equal(1, vm.DetailTabIndex); // Errors tab
    }

    [Fact]
    public void Test_failure_whose_source_is_not_in_the_patch_cannot_jump()
    {
        var vm = ViewModel(out var actions);
        actions.Snapshot = new PatchReviewSnapshot
        {
            Files = [new PatchReviewFile { Path = "src/App.cs", Operation = "replace" }],
        };
        var interaction = Held(PatchTransactionStatuses.NeedsRevision) with
        {
            TestFailures = [new PatchTestFailure { Code = "T", Path = "external/Other.cs", Line = 1, Message = "x" }],
        };

        vm.Update(interaction);

        Assert.False(Assert.Single(vm.TestFailures).CanJump);
    }

    [Fact]
    public void Accept_enables_immediately_after_tests_pass_and_failures_disappear()
    {
        var vm = ViewModel(out var actions);
        actions.Snapshot = new PatchReviewSnapshot
        {
            Files = [new PatchReviewFile { Path = "src/MyTests.cs", Operation = "replace" }],
        };
        vm.Update(Held(PatchTransactionStatuses.NeedsRevision) with
        {
            TestFailures = [new PatchTestFailure { Code = "T", Path = "src/MyTests.cs", Line = 2, Message = "failed" }],
        });
        Assert.False(vm.AcceptCommand.CanExecute(null));

        vm.Update(Held(PatchTransactionStatuses.AwaitingAcceptance) with { Revision = 2 });

        Assert.False(vm.HasTestFailures);
        Assert.True(vm.AcceptCommand.CanExecute(null));
    }

    [Fact]
    public void Same_patch_refresh_preserves_comments_tab_when_comments_remain()
    {
        var vm = ViewModel(out _);
        vm.Update(Held(PatchTransactionStatuses.NeedsRevision));
        vm.AddComment(3, "keep me here");
        Assert.Equal(4, vm.DetailTabIndex);

        var refreshed = Held(PatchTransactionStatuses.NeedsRevision) with
        {
            Revision = 2,
            BuildErrors = [new PatchBuildError { Code = "CS1002", Path = "src/File.cs", Line = 1, Message = "bad" }],
        };
        vm.Update(refreshed);

        Assert.Equal(4, vm.DetailTabIndex);
    }

    [Fact]
    public void Same_patch_refresh_preserves_tests_tab_even_when_build_errors_appear()
    {
        var vm = ViewModel(out var actions);
        actions.Snapshot = new PatchReviewSnapshot
        {
            Files = [new PatchReviewFile { Path = "src/MyTests.cs", Operation = "replace" }],
        };
        var withTests = Held(PatchTransactionStatuses.NeedsRevision) with
        {
            TestFailures = [new PatchTestFailure { Code = "T", Path = "src/MyTests.cs", Line = 2, Message = "failed" }],
        };
        vm.Update(withTests);
        Assert.Equal(3, vm.DetailTabIndex);

        var withBuildAndTests = withTests with
        {
            Revision = 2,
            BuildErrors = [new PatchBuildError { Code = "CS1002", Path = "src/App.cs", Line = 1, Message = "bad" }],
        };
        vm.Update(withBuildAndTests);

        Assert.Equal(3, vm.DetailTabIndex);
    }

    [Fact]
    public void Same_patch_refresh_falls_back_when_selected_tab_disappears()
    {
        var vm = ViewModel(out var actions);
        actions.Snapshot = new PatchReviewSnapshot
        {
            Files = [new PatchReviewFile { Path = "src/MyTests.cs", Operation = "replace" }],
        };
        var withTests = Held(PatchTransactionStatuses.NeedsRevision) with
        {
            TestFailures = [new PatchTestFailure { Code = "T", Path = "src/MyTests.cs", Line = 2, Message = "failed" }],
        };
        vm.Update(withTests);
        Assert.Equal(3, vm.DetailTabIndex);

        vm.Update(Held(PatchTransactionStatuses.AwaitingAcceptance) with { Revision = 2 });

        Assert.Equal(0, vm.DetailTabIndex);
    }

    [Fact]
    public void Different_patch_uses_default_failure_tab_selection()
    {
        var vm = ViewModel(out _);
        vm.Update(Held(PatchTransactionStatuses.AwaitingAcceptance));
        vm.DetailTabIndex = 0;

        var nextPatch = Held(PatchTransactionStatuses.NeedsRevision) with
        {
            PatchId = "p-2",
            BuildErrors = [new PatchBuildError { Code = "CS1002", Path = "src/File.cs", Line = 1, Message = "bad" }],
        };
        vm.Update(nextPatch);

        Assert.Equal(1, vm.DetailTabIndex);
    }

    [Fact]
    public void Adding_then_removing_a_comment_updates_state_and_tab()
    {
        var vm = ViewModel(out _);
        vm.Update(Held(PatchTransactionStatuses.NeedsRevision));
        Assert.False(vm.HasComments);

        vm.AddComment(7, "fix this");

        var comment = Assert.Single(vm.Comments);
        Assert.True(vm.HasComments);
        Assert.Equal("src/File.cs", comment.Path);
        Assert.Equal(7, comment.Line);
        Assert.Equal(4, vm.DetailTabIndex); // Comments tab

        vm.RemoveCommentCommand.Execute(comment);

        Assert.False(vm.HasComments);
    }

    [Fact]
    public void Open_issue_comment_blocks_accept_until_resolved_by_response()
    {
        var vm = ViewModel(out _);
        vm.Update(Held(PatchTransactionStatuses.AwaitingAcceptance));
        Assert.True(vm.AcceptCommand.CanExecute(null));

        vm.AddComment(7, "this blocks acceptance", openIssue: true);

        var comment = Assert.Single(vm.Comments);
        Assert.True(comment.OpenIssue);
        Assert.True(vm.HasOpenIssues);
        Assert.False(vm.AcceptCommand.CanExecute(null));

        vm.RespondToComment(comment, "resolved", resolveIssue: true);

        Assert.False(comment.OpenIssue);
        Assert.False(vm.HasOpenIssues);
        Assert.True(vm.AcceptCommand.CanExecute(null));
    }

    [Fact]
    public async Task Send_delivers_collected_comments_to_the_actions()
    {
        var vm = ViewModel(out var actions);
        vm.Update(Held(PatchTransactionStatuses.NeedsRevision));
        vm.AddComment(7, "fix this");
        var comment = Assert.Single(vm.Comments);

        await vm.SendCommand.ExecuteAsync(null);

        var sent = Assert.Single(actions.LastComments!);
        Assert.Equal("fix this", sent.Comment);
        Assert.Equal(7, sent.Line);
        Assert.Equal("src/File.cs", sent.Path);
        Assert.False(sent.OpenIssue);
        Assert.NotEmpty(sent.Id);
        Assert.False(comment.Pending); // delivered -> no longer pending
    }

    [Fact]
    public async Task Send_carries_open_issue_state()
    {
        var vm = ViewModel(out var actions);
        vm.Update(Held(PatchTransactionStatuses.NeedsRevision));
        vm.AddComment(7, "fix this", openIssue: true);

        await vm.SendCommand.ExecuteAsync(null);

        var sent = Assert.Single(actions.LastComments!);
        Assert.True(sent.OpenIssue);
    }

    [Fact]
    public async Task Send_uses_reanchored_comment_line_after_refresh()
    {
        var vm = ViewModel(out var actions);
        actions.FileContents["src/File.cs"] = "one\ntwo\ntarget\nfour\n";
        vm.Update(Held(PatchTransactionStatuses.NeedsRevision));
        vm.AddComment(3, "fix this");
        var comment = Assert.Single(vm.Comments);

        actions.FileContents["src/File.cs"] = "one\ninserted\ntwo\ntarget\nfour\n";
        vm.Update(Held(PatchTransactionStatuses.NeedsRevision) with { Revision = 2 });

        Assert.Equal(4, comment.Line);
        Assert.Equal(CommentAnchorStatus.Moved, comment.AnchorStatus);

        await vm.SendCommand.ExecuteAsync(null);

        var sent = Assert.Single(actions.LastComments!);
        Assert.Equal(4, sent.Line);
    }

    [Fact]
    public void Model_reply_appends_to_the_matching_comment_thread()
    {
        var vm = ViewModel(out _);
        vm.Update(Held(PatchTransactionStatuses.NeedsRevision)); // p-1, revision 1
        vm.AddComment(3, "why this?");
        var comment = Assert.Single(vm.Comments);

        var amended = Held(PatchTransactionStatuses.NeedsRevision) with
        {
            ReplyTurn = 1,
            CommentReplies = [new PatchCommentReply { Id = comment.Id, Reply = "because X" }],
        };
        vm.Update(amended);

        Assert.Equal(2, comment.Messages.Count);
        Assert.Equal(CommentAuthor.Model, comment.Messages[1].Author);
        Assert.Equal("because X", comment.Messages[1].Text);
    }

    [Fact]
    public void Model_reply_can_open_and_clear_existing_issue()
    {
        var vm = ViewModel(out _);
        vm.Update(Held(PatchTransactionStatuses.AwaitingAcceptance));
        vm.AddComment(3, "why this?");
        var comment = Assert.Single(vm.Comments);

        vm.Update(Held(PatchTransactionStatuses.AwaitingAcceptance) with
        {
            ReplyTurn = 1,
            CommentReplies = [new PatchCommentReply { Id = comment.Id, Reply = "I need clarification", OpenIssue = true }],
        });

        Assert.True(comment.OpenIssue);
        Assert.False(vm.AcceptCommand.CanExecute(null));

        vm.Update(Held(PatchTransactionStatuses.AwaitingAcceptance) with
        {
            ReplyTurn = 2,
            CommentReplies = [new PatchCommentReply { Id = comment.Id, Reply = "resolved", OpenIssue = false }],
        });

        Assert.False(comment.OpenIssue);
        Assert.True(vm.AcceptCommand.CanExecute(null));
    }

    [Fact]
    public void Unmatched_model_reply_creates_general_thread()
    {
        var vm = ViewModel(out _);
        vm.Update(Held(PatchTransactionStatuses.NeedsRevision));

        var amended = Held(PatchTransactionStatuses.NeedsRevision) with
        {
            ReplyTurn = 1,
            CommentReplies = [new PatchCommentReply { Id = "m-1", Reply = "Should I update docs too?" }],
        };
        vm.Update(amended);

        var comment = Assert.Single(vm.Comments);
        Assert.Equal("m-1", comment.Id);
        Assert.Equal("", comment.Path);
        Assert.Equal(0, comment.Line);
        Assert.False(comment.HasAnchor);
        Assert.Equal("General", comment.Location);
        Assert.False(comment.Pending);
        Assert.False(comment.OpenIssue);
        Assert.Equal(CommentAuthor.Model, comment.Messages[0].Author);
        Assert.Equal("Should I update docs too?", comment.Messages[0].Text);
    }

    [Fact]
    public void Unmatched_model_reply_can_create_open_issue_thread()
    {
        var vm = ViewModel(out _);
        vm.Update(Held(PatchTransactionStatuses.AwaitingAcceptance));

        vm.Update(Held(PatchTransactionStatuses.AwaitingAcceptance) with
        {
            ReplyTurn = 1,
            CommentReplies = [new PatchCommentReply { Id = "m-1", Reply = "Should I update docs too?", OpenIssue = true }],
        });

        var comment = Assert.Single(vm.Comments);
        Assert.True(comment.OpenIssue);
        Assert.True(vm.HasOpenIssues);
        Assert.False(vm.AcceptCommand.CanExecute(null));
    }

    [Fact]
    public void Unmatched_model_reply_can_create_anchored_thread()
    {
        var vm = ViewModel(out var actions);
        actions.FileContents["src/File.cs"] = "one\ntwo\ntarget\n";
        vm.Update(Held(PatchTransactionStatuses.NeedsRevision));

        var amended = Held(PatchTransactionStatuses.NeedsRevision) with
        {
            ReplyTurn = 1,
            CommentReplies = [new PatchCommentReply { Id = "m-1", Reply = "Question here", Path = "src/File.cs", Line = 3 }],
        };
        vm.Update(amended);

        var comment = Assert.Single(vm.Comments);
        Assert.True(comment.HasAnchor);
        Assert.Equal("src/File.cs", comment.Path);
        Assert.Equal(3, comment.Line);
        Assert.Equal("target", comment.AnchorText);
        Assert.Equal(CommentAuthor.Model, comment.Messages[0].Author);
    }

    [Fact]
    public async Task Reviewer_can_respond_to_model_originated_thread()
    {
        var vm = ViewModel(out var actions);
        vm.Update(Held(PatchTransactionStatuses.NeedsRevision) with
        {
            ReplyTurn = 1,
            CommentReplies = [new PatchCommentReply { Id = "m-1", Reply = "Should I update docs too?" }],
        });
        var comment = Assert.Single(vm.Comments);

        vm.RespondToComment(comment, "yes, please");
        await vm.SendCommand.ExecuteAsync(null);

        var sent = Assert.Single(actions.LastComments!);
        Assert.Equal("m-1", sent.Id);
        Assert.Equal("", sent.Path);
        Assert.Equal(0, sent.Line);
        Assert.Equal("yes, please", sent.Comment);
    }

    [Fact]
    public void Reply_only_turn_threads_even_when_revision_is_unchanged()
    {
        var vm = ViewModel(out _);
        vm.Update(Held(PatchTransactionStatuses.AwaitingAcceptance)); // revision 1, reply turn 0
        vm.AddComment(3, "q");
        var comment = Assert.Single(vm.Comments);

        // A reply-only amend keeps the revision but advances the reply turn.
        var replyTurn = Held(PatchTransactionStatuses.AwaitingAcceptance) with
        {
            Revision = 1,
            ReplyTurn = 1,
            CommentReplies = [new PatchCommentReply { Id = comment.Id, Reply = "answer" }],
        };
        vm.Update(replyTurn);

        Assert.Equal(2, comment.Messages.Count);
        Assert.Equal("answer", comment.Messages[1].Text);
    }

    [Fact]
    public void Model_reply_is_applied_once_across_a_refresh()
    {
        var vm = ViewModel(out _);
        vm.Update(Held(PatchTransactionStatuses.NeedsRevision));
        vm.AddComment(3, "q");
        var comment = Assert.Single(vm.Comments);
        var amended = Held(PatchTransactionStatuses.NeedsRevision) with
        {
            ReplyTurn = 1,
            CommentReplies = [new PatchCommentReply { Id = comment.Id, Reply = "a" }],
        };

        vm.Update(amended);
        vm.Update(amended); // a Refresh re-projects the same interaction/reply turn

        Assert.Equal(2, comment.Messages.Count); // model reply applied once, not doubled
    }

    [Fact]
    public void Respond_appends_a_reviewer_message_and_marks_pending()
    {
        var vm = ViewModel(out _);
        vm.Update(Held(PatchTransactionStatuses.NeedsRevision));
        vm.AddComment(3, "q");
        var comment = Assert.Single(vm.Comments);
        comment.Pending = false; // pretend the first message was already delivered

        vm.RespondToComment(comment, "follow-up");

        Assert.True(comment.Pending);
        Assert.True(vm.HasPendingComments);
        Assert.Equal(CommentAuthor.Reviewer, comment.Messages[^1].Author);
        Assert.Equal("follow-up", comment.Messages[^1].Text);
    }

    [Fact]
    public void Comments_clear_when_a_different_patch_opens()
    {
        var vm = ViewModel(out _);
        vm.Update(Held(PatchTransactionStatuses.NeedsRevision)); // PatchId p-1
        vm.AddComment(1, "a");
        Assert.True(vm.HasComments);

        vm.Update(Held(PatchTransactionStatuses.NeedsRevision) with { PatchId = "p-2" });

        Assert.False(vm.HasComments);
    }

    [Fact]
    public void RestoreState_rebuilds_comment_threads_and_interaction()
    {
        var vm = ViewModel(out _);
        var state = new HeldReviewState
        {
            Interaction = Held(PatchTransactionStatuses.NeedsRevision) with { ReplyTurn = 2 },
            Comments =
            [
                new ReviewCommentState
                {
                    Id = "c-1",
                    Path = "src/File.cs",
                    Line = 4,
                    Pending = true,
                    OpenIssue = true,
                    AnchorStatus = CommentAnchorStatus.Changed,
                    AnchorText = "q-line",
                    BeforeContext = ["before"],
                    AfterContext = ["after"],
                    Messages =
                    [
                        new CommentMessageState { Author = CommentAuthor.Reviewer, AuthorLabel = "You", Text = "q" },
                        new CommentMessageState { Author = CommentAuthor.Model, AuthorLabel = "ChatGPT", Text = "a" },
                    ],
                },
            ],
        };

        vm.RestoreState(state);

        Assert.Equal("p-1", vm.PatchId);
        var comment = Assert.Single(vm.Comments);
        Assert.True(comment.Pending);
        Assert.True(comment.OpenIssue);
        Assert.Equal("src/File.cs", comment.Path);
        Assert.Equal(CommentAnchorStatus.Changed, comment.AnchorStatus);
        Assert.Equal("q-line", comment.AnchorText);
        Assert.Equal(["before"], comment.BeforeContext);
        Assert.Equal(["after"], comment.AfterContext);
        Assert.Equal(2, comment.Messages.Count);
    }

    [Fact]
    public void RestoreState_sets_reply_watermark_so_existing_replies_are_not_reapplied()
    {
        var vm = ViewModel(out _);
        var interaction = Held(PatchTransactionStatuses.NeedsRevision) with
        {
            ReplyTurn = 3,
            CommentReplies = [new PatchCommentReply { Id = "c-1", Reply = "a" }],
        };
        vm.RestoreState(new HeldReviewState
        {
            Interaction = interaction,
            Comments =
            [
                new ReviewCommentState
                {
                    Id = "c-1",
                    Path = "src/File.cs",
                    Line = 4,
                    Messages =
                    [
                        new CommentMessageState { Author = CommentAuthor.Reviewer, AuthorLabel = "You", Text = "q" },
                        new CommentMessageState { Author = CommentAuthor.Model, AuthorLabel = "ChatGPT", Text = "a" },
                    ],
                },
            ],
        });

        vm.Update(interaction);

        Assert.Equal(2, Assert.Single(vm.Comments).Messages.Count);
    }

    [Fact]
    public async Task Changed_fires_for_review_mutations()
    {
        var vm = ViewModel(out _);
        var changed = 0;
        vm.Changed += (_, _) => changed++;

        vm.Update(Held(PatchTransactionStatuses.NeedsRevision));
        Assert.True(changed > 0);

        changed = 0;
        vm.AddComment(3, "q");
        Assert.True(changed > 0);

        changed = 0;
        var comment = Assert.Single(vm.Comments);
        await vm.SendCommand.ExecuteAsync(null);
        Assert.True(changed > 0);

        changed = 0;
        vm.RespondToComment(comment, "follow-up");
        Assert.True(changed > 0);

        changed = 0;
        vm.RemoveCommentCommand.Execute(comment);
        Assert.True(changed > 0);
    }

    private sealed class RecordingActions : IHeldPatchActions
    {
        public IReadOnlyList<ReviewerComment>? LastComments { get; private set; }
        public int AcceptCount { get; private set; }
        public int RevertCount { get; private set; }
        public int RefreshCount { get; private set; }
        public PatchReviewSnapshot Snapshot { get; set; } = new()
        {
            Files = [new PatchReviewFile { Path = "src/File.cs", Operation = "replace" }],
        };
        public string? FileDiff { get; set; }
        public string? LastDiffPath { get; private set; }
        public Dictionary<string, string> FileContents { get; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["src/File.cs"] = "one\ntwo\nthree\nfour\nfive\nsix\nseven\n",
        };

        public Task SendAsync(IReadOnlyList<ReviewerComment> comments)
        {
            LastComments = comments;
            return Task.CompletedTask;
        }
        public Task AcceptAsync() { AcceptCount++; return Task.CompletedTask; }
        public Task RevertAsync() { RevertCount++; return Task.CompletedTask; }
        public Task RefreshAsync() { RefreshCount++; return Task.CompletedTask; }
        public PatchReviewSnapshot GetSnapshot() => Snapshot;
        public string? GetFileDiff(string path) { LastDiffPath = path; return FileDiff; }
        public string? GetFileContent(string path) => FileContents.GetValueOrDefault(path);
    }

    private static PatchTreeNode FindFile(IReadOnlyList<PatchTreeNode> nodes, string path)
    {
        foreach (var node in nodes)
        {
            if (!node.IsFolder && string.Equals(node.RelativePath, path, StringComparison.OrdinalIgnoreCase))
                return node;

            if (node.IsFolder)
            {
                try
                {
                    return FindFile(node.Children, path);
                }
                catch (InvalidOperationException)
                {
                }
            }
        }

        throw new InvalidOperationException($"File node not found: {path}");
    }
}
