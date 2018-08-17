using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Diagnostics;

using Xunit;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using Moq;

using LaPlay.Infrastructure.Shell;

namespace LaPlay.Infrastructure.Synchronization
{
    public class SynchronizationAdapterTest
    {
        [Fact]
        public void LSFile_ShouldConstructWithTreeCommandResultLine()
        {
            List<dynamic> files = new List<dynamic> {
                new {expectedIdenfication = "regularFile", typeChar="-", bytes = new Random().Next(), date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), path = "/home/user/desktop"},
                new {expectedIdenfication = "directory", typeChar="d", bytes = new Random().Next(), date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), path = "/home/user/desktop"},
                new {expectedIdenfication = "characterDeviceFile", typeChar="c", bytes = new Random().Next(), date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), path = "/home/user/desktop"},
                new {expectedIdenfication = "blockDeviceFile", typeChar="b", bytes = new Random().Next(), date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), path = "/home/user/desktop"},
                new {expectedIdenfication = "localSocketFile", typeChar="s", bytes = new Random().Next(), date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), path = "/home/user/desktop"},
                new {expectedIdenfication = "namedPipe", typeChar="p", bytes = new Random().Next(), date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), path = "/home/user/desktop"},
                new {expectedIdenfication = "symbolicLink", typeChar="l", bytes = new Random().Next(), date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), path = "/home/user/desktop"}
            };

            var filesWithLSFiles = files.Select(file => new{file = file, lsFile = new SynchronizationAdapter.LSFile("[" + file.typeChar + "rwxrwxrwx      " + file.bytes + " " + file.date + "]  " + file.path + "\n")});
            
            filesWithLSFiles.ToList().ForEach(file => {
                Assert.True(file.file.expectedIdenfication == file.lsFile.type);
                Assert.True(file.file.bytes == file.lsFile.bytes);
                Assert.True(file.file.date == file.lsFile.modifiedOn.ToString("yyyy-MM-dd HH:mm:ss"));
                Assert.True(file.file.path == file.lsFile.path);
            });
        }

        [Fact]
        public void ListFiles_ShouldSucceed()
        {
            List<String> expectedResult = new LinuxAdapter().RunCommand("tree /etc  -a -D -f -i -p -s --timefmt \"%F %T\"").Split("\n").ToList();
            System.Text.RegularExpressions.Match counts = Regex.Match(expectedResult.ElementAt(expectedResult.Count() - 2), "([0-9]*).*, ([0-9]*).*");
            Int32 expectedDirectoryCount = Convert.ToInt32(counts.Groups[1].Value);
            Int32 expectedFileCount = Convert.ToInt32(counts.Groups[2].Value);

            SynchronizationAdapter synchronizationAdapter = new SynchronizationAdapter(new LinuxAdapter());

            List<SynchronizationAdapter.LSFile> files = synchronizationAdapter.ListFiles("/etc");

            Assert.True(expectedDirectoryCount + expectedFileCount == files.Count());
        }

        [Fact]
        public void FullJoin_ShouldSucceed()
        {
            Func<String> randomPath = () => String.Join("/", Enumerable.Range(1, new Random().Next(1, 10)).Select(i => (char) new Random().Next(65,90)));
            Func<SynchronizationAdapter.LSFile> randomPathLSFile = () => new SynchronizationAdapter.LSFile("[---------- 1024 0001-01-01 00:00:00]  " + randomPath.Invoke());

            List<SynchronizationAdapter.LSFile> mainFiles = new List<SynchronizationAdapter.LSFile>();
            List<SynchronizationAdapter.LSFile> mirrorFiles = new List<SynchronizationAdapter.LSFile>();

            List<SynchronizationAdapter.LSFile> leftFilesOnly = Enumerable.Range(1, new Random().Next(1, 10)).Select(i => randomPathLSFile.Invoke()).ToList();
            List<SynchronizationAdapter.LSFile> joinFilesOnly = Enumerable.Range(1, new Random().Next(1, 10)).Select(i => randomPathLSFile.Invoke()).ToList();
            List<SynchronizationAdapter.LSFile> rigthFilesOnly = Enumerable.Range(1, new Random().Next(1, 10)).Select(i => randomPathLSFile.Invoke()).ToList();

            leftFilesOnly.ForEach(file => mainFiles.Add(file));
            joinFilesOnly.ForEach(file => mainFiles.Add(file));
            joinFilesOnly.ForEach(file => mirrorFiles.Add(file));
            rigthFilesOnly.ForEach(file => mirrorFiles.Add(file));

            List<Tuple<SynchronizationAdapter.LSFile, SynchronizationAdapter.LSFile>> fullJoinResult = new SynchronizationAdapter(null).FullJoin(mainFiles, mirrorFiles);

            Assert.True(fullJoinResult.Count() == leftFilesOnly.Count() + joinFilesOnly.Count() + rigthFilesOnly.Count());

            Assert.True(fullJoinResult
                        .Where(line => line.Item1 != null && line.Item2 == null)
                        .Select(line => line.Item1.path)
                        .Intersect(leftFilesOnly.Select(file => file.path))
                        .Count() == leftFilesOnly.Count());

            Assert.True(fullJoinResult
                        .Where(line => line.Item1 != null && line.Item2 != null)
                        .Select(line => line.Item1.path)
                        .Intersect(joinFilesOnly.Select(file => file.path))
                        .Count() == joinFilesOnly.Count());

            Assert.True(fullJoinResult
                        .Where(line => line.Item1 == null && line.Item2 != null)
                        .Select(line => line.Item2.path)
                        .Intersect(rigthFilesOnly.Select(file => file.path))
                        .Count() == rigthFilesOnly.Count());
        }

        [Fact]
        public void CopyToMirror_ShouldSucceed()
        {
            Directory.CreateDirectory(Path.GetDirectoryName("/tmp/a1/long/path/to/a/file"));
            File.CreateText("/tmp/a1/long/path/to/a/file");

            SynchronizationAdapter.LSFile file = new SynchronizationAdapter.LSFile("[---------- 1024 0001-01-01 00:00:00]  /tmp/a1/long/path/to/a/file");

            new SynchronizationAdapter(new LinuxAdapter()).CopyToMirror("/tmp/a1", "/tmp/a2", file);
        }

        //[Fact]
        public void Synchronize_ShouldSucceed()
        {

            Mock<IShellContract> mockedShellContract = new Mock<IShellContract>();
            
            mockedShellContract.Setup(m => m.RunCommand(It.IsAny<String>())).Returns(
                "/tmp\n" +
                "[drwxrwxr-x        4096 2018-08-14]  /tmp/a\n" +
                "[drwxrwxr-x       20480 2018-08-14]  /tmp/appInsights-node\n" +
                "[srwxrwxrwx           0 2018-08-13]  /tmp/.X11-unix/X0\n" +
                "[-rw-------         410 2018-08-13]  /tmp/.xfsm-ICE-9C4VNZ\n" +
                "[drwxrwxrwt        4096 2018-08-13]  /tmp/.XIM-unix\n" +
                "[drwxrwxr-x        4096 2018-08-14]  /tmp/Z\n" +
                "[-rw-rw-r--           0 2018-08-14]  /tmp/Z/a\n" +
                "[-rw-rw-r--           0 2018-08-14]  /tmp/Z/b\n" +
                "[-rw-rw-r--       69632 2018-08-14]  /tmp/Z/tree.txt\n" +
                "%T [error opening dir]\n" +
                "\n" +
                "31 directories, 728 files\n" +
                ""
            );

            SynchronizationAdapter synchronizationAdapter = new SynchronizationAdapter(mockedShellContract.Object);

            synchronizationAdapter.ListFiles("");

            synchronizationAdapter.Synchronize("/media/sf_D_DRIVE/Apps/7-Zip/", "/media/sf_D_DRIVE/Apps/Audacity/");

            /*
            String sj = JsonConvert.SerializeObject(fullJoin);

            using(StreamWriter s = new StreamWriter("sj.txt", false))
            {
                foreach(var b in fullJoin)
                {
                    s.WriteLine(b.Item1?.Item1.ToString() + " | " + b.Item2?.Item1.ToString());
                    s.WriteLine(b.Item1?.Item2.ToString() + " | " + b.Item2?.Item2.ToString());
                    s.WriteLine(b.Item1?.Item3.ToString() + " | " + b.Item2?.Item3.ToString());
                    s.WriteLine(b.Item1?.Item4.ToString() + " | " + b.Item2?.Item4.ToString());
                }
            }
            */
        }
    }
}