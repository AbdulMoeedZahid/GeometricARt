using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GeoART.App.Save;
using GeoART.Contracts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GeoART.Tests.Save
{
    /// <summary>DesignLibrary: save, list, load and delete design files, each test in its own temp folder.</summary>
    public sealed class DesignLibraryTests
    {
        string _root;
        DesignLibrary _lib;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "GeoART-SaveTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _lib = new DesignLibrary(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        string[] JsonFiles() => Directory.GetFiles(_root, "*.json").Select(Path.GetFileName).ToArray();

        [Test]
        public void EmptyFolderGivesEmptyList()
        {
            Assert.That(_lib.List(), Is.Empty);
            Assert.That(_lib.NextDefaultName(), Is.EqualTo("Design 1"));
        }

        [Test]
        public void MissingFolderIsCreated()
        {
            string sub = Path.Combine(_root, "not", "there", "yet");
            var lib = new DesignLibrary(sub);
            Assert.That(lib.List(), Is.Empty);
            Assert.That(Directory.Exists(sub), Is.True);
        }

        [Test]
        public void SaveTwiceListsBothNewestFirst()
        {
            Assert.That(_lib.TrySave(GoldenBoards.Tiled2x2(), out DesignEntry first, out string error), Is.True, error);
            Assert.That(_lib.TrySave(GoldenBoards.StarOnFullBoard(), out DesignEntry second, out error), Is.True, error);
            Assert.That(first.Name, Is.EqualTo("Design 1"));
            Assert.That(second.Name, Is.EqualTo("Design 2"));
            Assert.That(second.Id, Is.Not.EqualTo(first.Id));
            StringAssert.StartsWith("design-", first.Id);

            var list = _lib.List();
            Assert.That(list.Count, Is.EqualTo(2));
            Assert.That(list[0].Name, Is.EqualTo("Design 2"), "newest first");
            Assert.That(list[1].Name, Is.EqualTo("Design 1"));
            Assert.That(list[0].Id, Is.EqualTo(second.Id));
            Assert.That(list[0].LayerCount, Is.EqualTo(3));
            Assert.That(list[0].ShapeCount, Is.EqualTo(13), "4 guides + circle + 8 star lines");
            Assert.That(list[1].LayerCount, Is.EqualTo(1));
            Assert.That(list[1].ShapeCount, Is.EqualTo(32));
            Assert.That(list[1].GridKind, Is.EqualTo(GridKind.Square));
            Assert.That(list[1].GridN, Is.EqualTo(2));
            Assert.That(list[0].SavedUtc, Is.EqualTo(second.SavedUtc));
            Assert.That(JsonFiles().Length, Is.EqualTo(2));
            Assert.That(Directory.GetFiles(_root, "*.tmp"), Is.Empty, "no temp file left behind");
            Assert.That(_lib.NextDefaultName(), Is.EqualTo("Design 3"));
        }

        [Test]
        public void CustomNameIsKept()
        {
            Assert.That(_lib.TrySave(GoldenBoards.Empty(), out DesignEntry e, out string error, "My star"), Is.True, error);
            Assert.That(_lib.List()[0].Name, Is.EqualTo("My star"));
            Assert.That(_lib.NextDefaultName(), Is.EqualTo("Design 1"));
        }

        [Test]
        public void LoadGivesTheSavedBoard()
        {
            var board = DesignSerializerTests.HandMade();
            Assert.That(_lib.TrySave(board, out DesignEntry e, out string error), Is.True, error);
            Assert.That(e.GridKind, Is.EqualTo(GridKind.Hex));
            Assert.That(e.GridN, Is.EqualTo(3));
            Assert.That(_lib.TryLoad(e.Id, out Board back, out error), Is.True, error);
            BoardAssert.AreEqual(board, back);
        }

        [Test]
        public void NewLibraryOnTheSameFolderSeesTheSaves()
        {
            Assert.That(_lib.TrySave(GoldenBoards.Tiled2x2(), out DesignEntry a, out string error), Is.True, error);
            Assert.That(_lib.TrySave(DesignSerializerTests.HandMade(), out DesignEntry b, out error), Is.True, error);
            var before = _lib.List();

            var again = new DesignLibrary(_root);
            var after = again.List();
            Assert.That(after.Select(x => x.Id), Is.EqualTo(before.Select(x => x.Id)));
            Assert.That(after.Select(x => x.Name), Is.EqualTo(before.Select(x => x.Name)));
            Assert.That(again.TryLoad(b.Id, out Board back, out error), Is.True, error);
            BoardAssert.AreEqual(DesignSerializerTests.HandMade(), back);
        }

        [Test]
        public void DeleteRemovesTheFileAndTheEntry()
        {
            Assert.That(_lib.TrySave(GoldenBoards.Tiled2x2(), out DesignEntry a, out string error), Is.True, error);
            Assert.That(_lib.TrySave(GoldenBoards.Empty(), out DesignEntry b, out error), Is.True, error);
            Assert.That(_lib.TryDelete(a.Id, out error), Is.True, error);
            var list = _lib.List();
            Assert.That(list.Count, Is.EqualTo(1));
            Assert.That(list[0].Id, Is.EqualTo(b.Id));
            Assert.That(JsonFiles(), Is.EqualTo(new[] { b.Id + ".json" }));

            Assert.That(_lib.TryDelete(a.Id, out error), Is.False, "already gone");
            Assert.That(error, Is.Not.Null.And.Not.Empty);
            Assert.That(_lib.TryLoad(a.Id, out Board none, out error), Is.False);
            Assert.That(none, Is.Null);
        }

        [Test]
        public void BrokenAndTempFilesAreSkipped()
        {
            Assert.That(_lib.TrySave(GoldenBoards.Tiled2x2(), out DesignEntry good, out string error), Is.True, error);
            File.WriteAllText(Path.Combine(_root, "design-broken.json"), "{ this is not json");
            File.WriteAllText(Path.Combine(_root, "design-half.json.tmp"), "{ \"version\": 1, \"na");
            File.WriteAllText(Path.Combine(_root, "notes.txt"), "hello");

            LogAssert.Expect(LogType.Warning, new Regex("design-broken\\.json"));
            var list = _lib.List();
            Assert.That(list.Count, Is.EqualTo(1));
            Assert.That(list[0].Id, Is.EqualTo(good.Id));

            _lib.List();   // the same broken file is not reported again
            LogAssert.NoUnexpectedReceived();
            Assert.That(_lib.TryLoad("design-broken", out Board b, out error), Is.False);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void BadIdsAreRejected()
        {
            // a real file just outside the folder, so "../<name>" would hit something if ids were not checked
            string outsideName = Path.GetFileName(_root) + "-outside";
            string outside = Path.Combine(Path.GetDirectoryName(_root), outsideName + ".json");
            File.WriteAllText(outside, "{}");
            try
            {
                foreach (string id in new[] { "../" + outsideName, "..\\" + outsideName, "a/b", @"a\b", "..", "", null, "c:x" })
                {
                    Assert.That(DesignLibrary.IsValidId(id), Is.False, id ?? "null");
                    Assert.That(_lib.TryLoad(id, out Board b, out string error), Is.False, id ?? "null");
                    Assert.That(error, Is.Not.Null.And.Not.Empty);
                    Assert.That(_lib.TryDelete(id, out error), Is.False, id ?? "null");
                }
                Assert.That(File.Exists(outside), Is.True);
            }
            finally
            {
                File.Delete(outside);
            }
        }
    }
}
