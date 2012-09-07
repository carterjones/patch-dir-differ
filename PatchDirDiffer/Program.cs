namespace PatchDirDiffer
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.IO;
    using System.Diagnostics;
    using DiffMatchPatch;
    using Nouzuru;

    class Program
    {
        private static void PrintUsage()
        {
            Console.WriteLine("usage: " + Process.GetCurrentProcess().ProcessName + " <unpatched dir> <patched dir>");
        }

        static void Main(string[] args)
        {
            if (args.Length != 2)
            {
                PrintUsage();
                return;
            }

            string unpatchedDir = args[0];
            string patchedDir = args[1];

            // Verify that the supplied directories are valid.
            if (!Directory.Exists(unpatchedDir))
            {
                Console.WriteLine(unpatchedDir + " is not a valid directory.");
                return;
            }
            else if (!Directory.Exists(patchedDir))
            {
                Console.WriteLine(patchedDir + " is not a valid directory.");
                return;
            }

            // Get the absolute directory paths.
            string[] absoluteUnpatchedDirFiles = Directory.GetFiles(unpatchedDir, "*", SearchOption.AllDirectories);
            string[] absolutePatchedDirFiles = Directory.GetFiles(patchedDir, "*", SearchOption.AllDirectories);

            // Convert the directory paths to relative directory paths.
            string[] relativeUnpatchedDirFiles =
                absoluteUnpatchedDirFiles.Select(x => x.Remove(0, unpatchedDir.Length)).ToArray();
            string[] relativePatchedDirFiles =
                absolutePatchedDirFiles.Select(x => x.Remove(0, patchedDir.Length)).ToArray();

            // Get a set of common relative directory paths.
            string[] sharedRelativeDirFiles = relativePatchedDirFiles.Intersect(relativeUnpatchedDirFiles).ToArray();
            
            // TODO: Display information about files that are not in one or the other relative directory lists.

            StringBuilder diffHtml = new StringBuilder();

            // Compare the files.
            foreach (string relativePath in sharedRelativeDirFiles)
            {
                string absoluteUnpatchedPath = unpatchedDir + relativePath;
                string absolutePatchedPath = patchedDir + relativePath;
                if (AreFileContentsEqual(absoluteUnpatchedPath, absolutePatchedPath))
                {
                    //Console.WriteLine("Equal: " + relativePath);
                }
                else
                {
                    Console.WriteLine("Not equal: " + relativePath);
                    // Do something like diffing based on filetype.
                    // e.g.:
                    //  - exe/dll: disassembled and then compared
                    //  - txt: simple text diff

                    string extension = Path.GetExtension(absoluteUnpatchedPath);
                    // Assume plaintext for the following:
                    // - no file extension
                    // - txt
                    // - xml

                    switch(extension)
                    {
                        // Plaintext
                        case ".txt":
                        case ".xml":
                        case "":
                            string unpatchedText = File.ReadAllText(absoluteUnpatchedPath);
                            string patchedText = File.ReadAllText(absolutePatchedPath);
                            diffHtml.Append(relativePath + ":<br />");
                            diffHtml.Append(OutputDiffToHtml(unpatchedText, patchedText));
                            diffHtml.Append("<br /><hr /><br />");
                            break;

                        // PE format
                        case ".dll":
                        case ".exe":
                            break;

                        // Other
                        default:
                            break;
                    }
                }
            }

            if (diffHtml.Length > 0)
            {
                File.WriteAllText("diff.html", diffHtml.ToString());
            }

            return;
        }

        /// <summary>
        /// Determines if two files have equal contents.
        /// </summary>
        /// <param name="f1">the first file being compared</param>
        /// <param name="f2">the second file being compared</param>
        /// <returns>true if the file contents are the same</returns>
        private static bool AreFileContentsEqual(string f1, string f2)
        {
            // Check filesize first.
            FileInfo fi1 = new FileInfo(f1);
            FileInfo fi2 = new FileInfo(f2);
            if (fi1.Length != fi2.Length)
            {
                return false;
            }

            // Check md5 sums next.
            string md51 = GetMD5FromFile(f1);
            string md52 = GetMD5FromFile(f2);
            if (!md51.Equals(md52))
            {
                return false;
            }

            // Check sha1 last.
            string sha11 = GetSHA1FromFile(f1);
            string sha12 = GetSHA1FromFile(f2);
            if (!sha11.Equals(sha12))
            {
                return false;
            }

            // At this point, the odds are large that the file contents are the same.
            return true;
        }

        /// <summary>
        /// Get the MD5 hash of the contents of the file.
        /// </summary>
        /// <param name="fileName">The file to examine.</param>
        /// <returns>the MD5 hash of the file contents</returns>
        private static string GetMD5FromFile(string fileName)
        {
            bool isReadOnly = false;
            FileInfo fi = new FileInfo(fileName);
            FileAttributes originalAttributes = fi.Attributes;
            if (fi.Attributes.HasFlag(FileAttributes.ReadOnly))
            {
                isReadOnly = true;
                FileAttributes newAttributes = originalAttributes ^ FileAttributes.ReadOnly;
                File.SetAttributes(fileName, newAttributes);
            }

            FileStream file = new FileStream(fileName, FileMode.Open);
            MD5 md5 = new MD5CryptoServiceProvider();
            byte[] retVal = md5.ComputeHash(file);
            file.Close();

            if (isReadOnly)
            {
                File.SetAttributes(fileName, originalAttributes);
            }

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < retVal.Length; i++)
            {
                sb.Append(retVal[i].ToString("x2"));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Get the SHA1 of the contents of the file.
        /// </summary>
        /// <param name="fileName">The file to examine.</param>
        /// <returns>the SHA1 of the file contents</returns>
        private static string GetSHA1FromFile(string fileName)
        {
            using (FileStream fs = new FileStream(fileName, FileMode.Open))
            {
                using (SHA1Managed sha1 = new SHA1Managed())
                {
                    byte[] hash = sha1.ComputeHash(fs);
                    StringBuilder formatted = new StringBuilder(hash.Length);
                    foreach (byte b in hash)
                    {
                        formatted.AppendFormat("{0:X2}", b);
                    }

                    return formatted.ToString();
                }
            }
        }

        private static string OutputDiffToHtml(string s1, string s2)
        {
            diff_match_patch dmp = new diff_match_patch();
            List<Diff> diffs = dmp.diff_main(s1, s2, true);
            dmp.diff_cleanupSemantic(diffs);
            return my_diff_prettyHtml(diffs);
        }

        /**
         * Convert a Diff list into a pretty HTML report.
         * @param diffs List of Diff objects.
         * @return HTML representation.
         */
        public static string my_diff_prettyHtml(List<Diff> diffs)
        {
            StringBuilder html = new StringBuilder();
            foreach (Diff aDiff in diffs)
            {
                string text =
                    aDiff.text.Replace("&", "&amp;")
                              .Replace("<", "&lt;")
                              .Replace(">", "&gt;")
                              .Replace(" ", "&nbsp;")
                              .Replace("\r", string.Empty)
                              .Replace("\n", "<br />");

                switch (aDiff.operation)
                {
                    case Operation.INSERT:
                        html.Append("<ins style=\"background:#e6ffe6;\">").Append(text).Append("</ins>");
                        break;
                    case Operation.DELETE:
                        html.Append("<del style=\"background:#ffe6e6;\">").Append(text).Append("</del>");
                        break;
                    case Operation.EQUAL:
                        html.Append("<span>").Append(text).Append("</span>");
                        break;
                }
            }

            return RemoveUnchangedLines(html.ToString());
        }

        private static string RemoveUnchangedLines(string html, int numLinesBefore = 1, int numLinesAfter = 1)
        {
            StringBuilder filteredLines = new StringBuilder();
            string[] lines = html.Split(new string[] { "<br />" }, StringSplitOptions.RemoveEmptyEntries);
            List<DiffedLine> diffedLines = new List<DiffedLine>();
            for (int i = 0; i < lines.Length; ++i)
            {
                DiffedLine dl = new DiffedLine();
                dl.IsChange =
                    lines[i].Contains("<ins ") |
                    lines[i].Contains("<del ") |
                    lines[i].Contains("</ins>") |
                    lines[i].Contains("</del>");
                dl.Show = dl.IsChange;
                dl.Text = lines[i];
                dl.LineNumber = i;
                diffedLines.Add(dl);
            }

            for (int i = 0; i < diffedLines.Count; ++i)
            {
                if (diffedLines[i].IsChange)
                {
                    for (int j = i - 1; j > 0 && j > i - 1 - numLinesBefore; --j)
                    {
                        diffedLines[j].Show = true;
                    }

                    for (int k = i + 1; k < diffedLines.Count && k < i + 1 + numLinesAfter; ++k)
                    {
                        diffedLines[k].Show = true;
                    }
                }
            }

            int lastShownLineNumber = -1;
            foreach (DiffedLine dl in diffedLines)
            {
                if (dl.Show)
                {
                    if (lastShownLineNumber != -1)
                    {
                        if (lastShownLineNumber + 1 != dl.LineNumber)
                        {
                            filteredLines.Append("...<br />");
                        }
                    }

                    lastShownLineNumber = dl.LineNumber;
                    if (dl.IsChange)
                    {
                        filteredLines.Append("<b><i>" + dl.LineNumber + "</i></b>: " + dl.Text + "<br />");
                    }
                    else
                    {
                        filteredLines.Append(dl.LineNumber + ": " + dl.Text + "<br />");
                    }
                }
            }

            return filteredLines.ToString();
        }

        private class DiffedLine
        {
            public bool Show { get; set; }
            public bool IsChange { get; set; }
            public string Text { get; set; }
            public int LineNumber { get; set; }
        }
    }
}
