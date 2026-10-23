#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using NeoCompose.Tests.Diagnostic423;
using NUnit.Framework;
namespace NeoCompose.Tests
{
    public class Neo423Tests
    {
        [TestCase("IndexedUnread")]
        [TestCase("ScanUnread")]
        [TestCase("IndexedAny")]
        [TestCase("ScanAny")]
        [TestCase("ScanLoop")]
        [TestCase("TupleAny")]
        [TestCase("TupleLoop")]
        public void MailboxTransitions(string watched)
        {
            using var client = NeoTestSaveStack.ClientFromSchema(NeoTestExport.Read(File.ReadAllText("Packages/com.ryanbliss.neocompose/Tests/Neo423Diagnostic/mailbox.json")), assumeCurrentSchema: false);
            using var project = new TestProjectNeo(client);
            var box = Probe.Box;
            var heard = new List<string>();
            string Read() => watched switch
            {
                "IndexedUnread" => box.IndexedUnread.ToString(),
                "ScanUnread" => box.ScanUnread.ToString(),
                "IndexedAny" => box.IndexedAny.ToString(),
                "ScanAny" => box.ScanAny.ToString(),
                "ScanLoop" => box.ScanLoop.ToString(),
                "TupleAny" => box.TupleAny.ToString(),
                _ => box.TupleLoop.ToString()
            };
            IDisposable sub = watched switch
            {
                "IndexedUnread" => box.OnChanged(Mailbox.Fields.IndexedUnread, (v, _) => heard.Add(v.ToString())),
                "ScanUnread" => box.OnChanged(Mailbox.Fields.ScanUnread, (v, _) => heard.Add(v.ToString())),
                "IndexedAny" => box.OnChanged(Mailbox.Fields.IndexedAny, (v, _) => heard.Add(v.ToString())),
                "ScanAny" => box.OnChanged(Mailbox.Fields.ScanAny, (v, _) => heard.Add(v.ToString())),
                "ScanLoop" => box.OnChanged(Mailbox.Fields.ScanLoop, (v, _) => heard.Add(v.ToString())),
                "TupleAny" => box.OnChanged(Mailbox.Fields.TupleAny, (v, _) => heard.Add(v.ToString())),
                _ => box.OnChanged(Mailbox.Fields.TupleLoop, (v, _) => heard.Add(v.ToString()))
            };
            using (sub)
            {
                string prior = "";
                var failures = new List<string>();
                void Snapshot(string step)
                {
                    var mails = box.Inbox.ToArray();
                    int unread = mails.Count(m => m.Status == MailStatus.Delivered);
                    int rewards = mails.Count(m => m.Status == MailStatus.Viewed && m.RewardResults.Any(r => r.Result == RewardStatus.Pending));
                    string expected = watched.StartsWith("Tuple") ? (unread, rewards).ToString() : watched.EndsWith("Unread") ? unread.ToString() : rewards.ToString();
                    string callback = heard.LastOrDefault() ?? prior;
                    string direct = Read();
                    if (direct != expected)
                        failures.Add(step + " direct " + direct + " expected " + expected);
                    if (step != "seed" && callback != expected)
                        failures.Add(step + " callback " + callback + " expected " + expected);
                    prior = direct;
                }
                Snapshot("seed");
                var seed = box.Inbox[0];
                seed.Status = MailStatus.Viewed;
                Snapshot("seed-viewed");
                seed.RewardResults[0].Result = RewardStatus.Claimed;
                Snapshot("seed-partial");
                seed.RewardResults[1].Result = RewardStatus.Claimed;
                Snapshot("seed-claimed");
                box.Inbox.Clear();
                Snapshot("clear");
                box.Inbox.Add(new Mail());
                box.Inbox.Add(new Mail());
                box.Inbox.Add(new Mail());
                Snapshot("refill-scheduled");
                var stored = box.Inbox.ToArray();
                stored[0].Status = MailStatus.Delivered;
                stored[1].Status = MailStatus.Delivered;
                Snapshot("new-delivered");
                stored[1].Status = MailStatus.Viewed;
                Snapshot("new-viewed");
                stored[1].RewardResults[0].Result = RewardStatus.Claimed;
                Snapshot("new-partial");
                stored[1].RewardResults[1].Result = RewardStatus.Claimed;
                Snapshot("new-claimed");
                var removed = stored[0];
                var retained = removed.Clone();
                Assert.That(box.Inbox.Remove(removed), Is.True);
                Snapshot("remove-delivered");
                box.Inbox.Add(retained);
                Snapshot("reinsert-delivered");
                var reinserted = box.Inbox.Single(m => m.Status == MailStatus.Delivered);
                reinserted.Status = MailStatus.Viewed;
                Snapshot("reinsert-viewed");
                reinserted.RewardResults.Clear();
                Snapshot("clear-rewards");
                reinserted.RewardResults.Add(new Reward());
                Snapshot("new-pending-reward");
                reinserted.RewardResults[0].Result = RewardStatus.Claimed;
                Snapshot("new-reward-claimed");
                int beforeDispose = heard.Count;
                sub.Dispose();
                reinserted.Status = MailStatus.Delivered;
                Assert.That(heard.Count, Is.EqualTo(beforeDispose), "Disposed subscription must not run.");
                Assert.That(Read(), Is.EqualTo(watched.StartsWith("Tuple") ? "(1, 0)" : watched.EndsWith("Unread") ? "1" : "0"));
                Assert.That(failures, Is.Empty, string.Join("\n", failures));
            }
        }
    }
}
