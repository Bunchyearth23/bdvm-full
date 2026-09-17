using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BDVM.Domain;

internal static class PersistentJournalChecks
{
    private sealed class MemoryStorage : IPersistentJournalStorage
    {
        public readonly Dictionary<string, string> Files = new Dictionary<string, string>(StringComparer.Ordinal);
        public bool FailBeforeWrite, FailAfterWrite;
        public void WriteOnce(string name, string content)
        {
            if (FailBeforeWrite) { FailBeforeWrite = false; throw new IOException("before write"); }
            if (Files.TryGetValue(name, out var existing) && existing != content) throw new InvalidDataException("record exists");
            Files[name] = content;
            if (FailAfterWrite) { FailAfterWrite = false; throw new IOException("after durable write, before ack"); }
        }
        public string Read(string name) => Files[name];
        public IReadOnlyList<string> Transactions() => Files.Keys.Where(value => value.EndsWith(".transaction", StringComparison.Ordinal)).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        public void Dispose() { }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Refused<T>(Action action, string message) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception(message); }
    private static PersistentMutation Change(string key, string? previous, string? value) => new PersistentMutation
        { System = "economy", Key = key, ExpectedHash = previous == null ? "" : PersistentJournalCodec.Hash(previous), Value = value };

    public static void Run()
    {
        var storage = new MemoryStorage();
        using (var journal = PersistentJournal.Create(storage, "career", new[] { new PersistentDocument { System = "economy", Key = "wallet", Value = "100" } }))
        {
            var first = journal.Commit("credit", new[] { Change("wallet", "100", "120") });
            Check(journal.Commit("credit", new[] { Change("wallet", "100", "120") }).Sequence == first.Sequence && journal.Sequence == 1, "duplicate credit was applied twice");
            Refused<InvalidOperationException>(() => journal.Commit("credit", new[] { Change("wallet", "100", "140") }), "operation conflict accepted");
            Refused<InvalidDataException>(() => journal.Commit("conflict", new[] { Change("wallet", "120", "140"), Change("stock", "wrong", "10") }), "partial conflicting transaction accepted");
            Check(journal.Read("economy", "wallet") == "120" && journal.Sequence == 1, "conflicting transaction partially changed projection");
            storage.FailBeforeWrite = true;
            Refused<IOException>(() => journal.Commit("io-before", new[] { Change("wallet", "120", "130") }), "write failure hidden");
            Check(journal.Read("economy", "wallet") == "120", "unpersisted value was published");
            storage.FailAfterWrite = true;
            Refused<IOException>(() => journal.Commit("io-after", new[] { Change("wallet", "120", "130") }), "lost ack failure hidden");
            Check(journal.Sequence == 1, "value published without append acknowledgement");
            Check(journal.Commit("io-after", new[] { Change("wallet", "120", "130") }).Sequence == 2, "retry after lost append ack failed");
            var saved = PersistentJournalCodec.Unpack<PersistentCheckpoint>(journal.PrepareCheckpoint());
            journal.Commit("future", new[] { Change("wallet", "130", "999") });
            using (var fork = PersistentJournal.Fork(new MemoryStorage(), saved, "career"))
            {
                Check(fork.Read("economy", "wallet") == "130" && fork.Sequence == 2 && fork.BranchId != journal.BranchId, "old save loaded future events");
                Check(fork.Commit("io-after", new[] { Change("wallet", "120", "130") }).Sequence == 2, "old save lost idempotent receipts");
            }
            Refused<InvalidDataException>(() => PersistentJournal.Fork(new MemoryStorage(), saved, "another-career"), "cross-career checkpoint accepted");
            Check(Task.Run(() => { try { journal.Read("economy", "wallet"); return false; } catch (InvalidOperationException) { return true; } }).Result, "journal live data was accessible from another thread");
        }
        using (var recovered = PersistentJournal.Recover(storage, "career"))
            Check(recovered.Sequence == 3 && recovered.Read("economy", "wallet") == "999", "durable events did not recover exactly");
        var last = storage.Transactions().Last();
        storage.Files[last] = storage.Files[last].Replace("999", "998");
        Refused<InvalidDataException>(() => PersistentJournal.Recover(storage, "career"), "corrupt durable event accepted");

        using (var entered = new ManualResetEventSlim())
        using (var release = new ManualResetEventSlim())
        {
            var stateOwner = new PersistentStateOwner(() => PersistentJournal.Create(new MemoryStorage(), "queue", Array.Empty<PersistentDocument>()), 2, 8);
            var active = stateOwner.Enqueue(journal => { entered.Set(); release.Wait(); return Thread.CurrentThread.ManagedThreadId; });
            Check(entered.Wait(TimeSpan.FromSeconds(5)), "owner did not start");
            var one = stateOwner.Enqueue(journal => journal.Commit("one", new[] { Change("wallet", null, "1") }).Sequence, 4);
            var two = stateOwner.Enqueue(journal => journal.Commit("two", new[] { Change("wallet", "1", "2") }).Sequence, 4);
            Refused<InvalidOperationException>(() => stateOwner.Enqueue(journal => 3, 1).GetAwaiter().GetResult(), "queue byte overflow executed inline");
            var stop = stateOwner.StopAsync();
            Check(!stop.IsCompleted, "stop waited or pretended the running transaction was done");
            release.Set();
            Check(active.Result != Thread.CurrentThread.ManagedThreadId && one.Result == 1 && two.Result == 2, "owner thread or FIFO order violated");
            stop.GetAwaiter().GetResult();
            Check(stateOwner.PendingBytes == 0, "queue byte accounting leaked");
            Refused<ObjectDisposedException>(() => stateOwner.Enqueue(journal => 0).GetAwaiter().GetResult(), "stopped owner accepted work");
        }
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        var directory = Path.GetFullPath(Path.Combine(temporaryRoot, "bdvm-journal-test-" + Guid.NewGuid().ToString("N")));
        Check(directory.StartsWith(temporaryRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "test directory escapes temp root");
        try
        {
            using (var journal = PersistentJournal.Create(new FilePersistentJournalStorage(directory), "files", Array.Empty<PersistentDocument>()))
            {
                Refused<IOException>(() => new FilePersistentJournalStorage(directory), "second writer acquired journal");
                journal.Commit("entry", new[] { Change("wallet", null, "5") });
                journal.PrepareCheckpoint();
            }
            File.WriteAllText(Path.Combine(directory, "pending-interrupted.tmp"), "not committed");
            using (var journal = PersistentJournal.Recover(new FilePersistentJournalStorage(directory), "files"))
                Check(journal.Read("economy", "wallet") == "5", "file recovery failed or replayed an incomplete temporary write");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        var segmentedDirectory = Path.GetFullPath(Path.Combine(temporaryRoot, "bdvm-segment-test-" + Guid.NewGuid().ToString("N")));
        Check(segmentedDirectory.StartsWith(temporaryRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "segment test cleanup escapes temp root");
        try
        {
            using (var journal = PersistentJournal.Create(new SegmentedPersistentJournalStorage(segmentedDirectory), "segmented", Array.Empty<PersistentDocument>()))
                for (var number = 1; number <= 260; number++) journal.Commit("entry-" + number, new[] { Change("wallet", number == 1 ? null : (number - 1).ToString(), number.ToString()) });
            var segments = Directory.GetFiles(segmentedDirectory, "*.segment").OrderBy(value => value, StringComparer.Ordinal).ToArray();
            Check(segments.Length == 2 && Directory.GetFiles(segmentedDirectory, "*.transaction").Length == 0,
                "journal creates bounded segments instead of one file per economic second");
            using (var stream = new FileStream(segments.Last(), FileMode.Append, FileAccess.Write)) stream.Write(new byte[] { 0x42, 0x56, 0x4a }, 0, 3);
            using (var recovered = PersistentJournal.Recover(new SegmentedPersistentJournalStorage(segmentedDirectory), "segmented"))
            {
                Check(recovered.Sequence == 260 && recovered.Read("economy", "wallet") == "260", "torn terminal segment append damaged committed records");
                recovered.Commit("after-torn-tail", new[] { Change("wallet", "260", "261") });
            }
            using (var recovered = PersistentJournal.Recover(new SegmentedPersistentJournalStorage(segmentedDirectory), "segmented"))
                Check(recovered.Sequence == 261 && recovered.Read("economy", "wallet") == "261", "append after interrupted tail did not recover");
            using (var stream = new FileStream(segments.First(), FileMode.Open, FileAccess.Write)) stream.SetLength(stream.Length - 1);
            Refused<InvalidDataException>(() => PersistentJournal.Recover(new SegmentedPersistentJournalStorage(segmentedDirectory), "segmented"),
                "a corrupt nonterminal segment was silently skipped");
            // A rejected recovery must release the writer lock as well.
            using (var probe = new FilePersistentJournalStorage(segmentedDirectory)) { }
        }
        finally { if (Directory.Exists(segmentedDirectory)) Directory.Delete(segmentedDirectory, true); }
        Console.WriteLine("PASS persistent journal: durable append/retry, atomic conflicts, integrity, old-save fork, replay, owner thread, bounded FIFO and exclusive file writer.");
        Console.WriteLine("PASS segmented journal: 260 transactions in 2 segments; torn-tail recovery, continued append, corruption refusal and failed-open lock cleanup.");
    }
}
