namespace PatchDirDiffer
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.RegularExpressions;
    using Bunseki;
    using DiffMatchPatch;

    /// <summary>
    /// The main program class to be run upon execution.
    /// </summary>
    public class Program
    {
        /// <summary>
        /// The main function that is run upon execution.
        /// </summary>
        /// <remarks>
        /// TODO: Display information about files that are not in one or the other relative directory lists.
        /// </remarks>
        /// <param name="args">Command line arguments passed to the program.</param>
        public static void Main(string[] args)
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

            StringBuilder totalHtml = new StringBuilder();
            StringBuilder currentHtml = new StringBuilder();
            List<ChangeStats> totalStats = new List<ChangeStats>();

            // Clear the destination file.
            File.Delete("diff.html");

            // Compare the files.
            foreach (string relativePath in sharedRelativeDirFiles)
            {
                string absoluteUnpatchedPath = unpatchedDir + relativePath;
                string absolutePatchedPath = patchedDir + relativePath;
                if (!AreFileContentsEqual(absoluteUnpatchedPath, absolutePatchedPath))
                {
                    Console.WriteLine("Not equal: " + relativePath);
                    string extension = Path.GetExtension(absoluteUnpatchedPath);
                    string unpatchedText = string.Empty;
                    string patchedText = string.Empty;
                    string diffData = string.Empty;
                    ChangeStats cs = new ChangeStats();
                    cs.RelativePath = relativePath;

                    switch (extension)
                    {
                        // Plaintext
                        case ".txt":
                        case ".xml":
                        case "":
                            unpatchedText = File.ReadAllText(absoluteUnpatchedPath);
                            patchedText = File.ReadAllText(absolutePatchedPath);
                            diffData = GetDiffAsHtml(unpatchedText, patchedText, cs);
                            break;

                        // PE format
                        case ".dll":
                        case ".exe":
                            byte[] unpatchedBytes = File.ReadAllBytes(absoluteUnpatchedPath);
                            byte[] patchedBytes = File.ReadAllBytes(absolutePatchedPath);
                            Disassembler d = new Disassembler();
                            d.Engine = Disassembler.InternalDisassembler.BeaEngine;

                            try
                            {
                                IEnumerable<Instruction> unpatchedInstructions =
                                    d.DisassembleInstructions(unpatchedBytes);
                                IEnumerable<Instruction> patchedInstructions =
                                    d.DisassembleInstructions(patchedBytes);
                                unpatchedText = string.Join("\n", unpatchedInstructions.Select(x => x.ToString()));
                                patchedText = string.Join("\n", patchedInstructions.Select(x => x.ToString()));
                                diffData = GetDiffAsHtml(unpatchedText, patchedText, cs);
                            }
                            catch (Exception e)
                            {
                                unpatchedText = string.Empty;
                                patchedText = string.Empty;
                                diffData = e.Message;
                            }

                            break;

                        // Other
                        default:
                            unpatchedText = string.Empty;
                            patchedText = string.Empty;
                            diffData = string.Empty;
                            break;
                    }

                    currentHtml.Append("<a name=\"" + cs.PathHash + "\">" + relativePath + "</a>:<br />");
                    currentHtml.Append(diffData);
                    currentHtml.Append("<br /><hr /><br />");
                    File.AppendAllText("diff.html", currentHtml.ToString());
                    totalHtml.Append(currentHtml.ToString());
                    totalStats.Add(cs);
                    currentHtml.Clear();
                }
            }

            // Display diff stats in a table at the top. Re-write the whole file to add this information.
            string introHtml =
                "<html><head><script src=\"sorttable.js\"></script><style>" +
                "table.sortable thead { background-color:#eee; color:#666666; font-weight: bold; cursor: default; }" +
                "</style></head><body>";
            totalHtml.Insert(0, introHtml);
            StringBuilder statsHtml = new StringBuilder();
            statsHtml.Append("<table border=\"1\" class=\"sortable\">");
            statsHtml.Append("<tr>");
            statsHtml.Append("<td>Relative Path</td>");
            statsHtml.Append("<td>Percent Change</td>");
            statsHtml.Append("<td># bytes deleted</td>");
            statsHtml.Append("<td># bytes inserted</td>");
            statsHtml.Append("<td>total # bytes (unpatched)");
            statsHtml.Append("<td>total # bytes (patched)");
            statsHtml.Append("</tr>");
            foreach (ChangeStats cs in totalStats)
            {
                statsHtml.Append("<tr>");
                statsHtml.Append("<td><a href=\"#" + cs.PathHash + "\">" + cs.RelativePath + "</a></td>");
                statsHtml.Append("<td>" + string.Format("{0:0.0000}%", cs.PercentChanged) + "</td>");
                statsHtml.Append("<td>" + cs.NumBytesDeleted + "</td>");
                statsHtml.Append("<td>" + cs.NumBytesInserted + "</td>");
                statsHtml.Append("<td>" + cs.NumBytesOld + "</td>");
                statsHtml.Append("<td>" + cs.NumBytesNew + "</td>");
                statsHtml.Append("</tr>");
            }

            statsHtml.Append("</table><br /><hr /><br />");
            totalHtml.Insert(0, statsHtml.ToString());
            totalHtml.Append("</body></html>");

            // Write the table to the top of the diff file.
            File.WriteAllText("diff.html", totalHtml.ToString());

            return;
        }

        /// <summary>
        /// Prints the command line usage instructions to the console.
        /// </summary>
        private static void PrintUsage()
        {
            Console.WriteLine("usage: " + Process.GetCurrentProcess().ProcessName + " <unpatched dir> <patched dir>");
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
        /// Retrieves the MD5 hash of the supplied string.
        /// </summary>
        /// <param name="s">The string to be hashed.</param>
        /// <returns>the MD5 hash of the string</returns>
        private static string GetMD5OfString(string s)
        {
            MemoryStream ms = new MemoryStream();
            MD5 md5 = new MD5CryptoServiceProvider();
            byte[] retVal = md5.ComputeHash(s.GetBytes());

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < retVal.Length; i++)
            {
                sb.Append(retVal[i].ToString("x2"));
            }

            return sb.ToString();
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

        /// <summary>
        /// Discovers the differences between two strings and transforms the differences into HTML representation.
        /// </summary>
        /// <param name="s1">The first string to be compared.</param>
        /// <param name="s2">The second string to be compared.</param>
        /// <param name="cs">Change statistics that will be set during the diffing process.</param>
        /// <returns>the HTML representation of the differences between the two strings</returns>
        private static string GetDiffAsHtml(string s1, string s2, ChangeStats cs)
        {
            diff_match_patch dmp = new diff_match_patch();
            List<Diff> diffs = dmp.diff_main(s1, s2, true);
            dmp.diff_cleanupSemantic(diffs);
            return DiffsToHtml(diffs, cs);
        }

        /// <summary>
        /// Convert a Diff list into a pretty HTML report.
        /// </summary>
        /// <param name="diffs">The diffs to be converted to HTML format.</param>
        /// <param name="cs">The change statistics for this set of Diffs.</param>
        /// <returns>the HTML representation of the diffs</returns>
        private static string DiffsToHtml(List<Diff> diffs, ChangeStats cs)
        {
            StringBuilder html = new StringBuilder();
            ulong numBytesInserted = 0;
            ulong numBytesDeleted = 0;
            ulong numBytesEqual = 0;
            foreach (Diff diff in diffs)
            {
                string text =
                    diff.text.Replace("&", "&amp;")
                              .Replace("<", "&lt;")
                              .Replace(">", "&gt;")
                              .Replace(" ", "&nbsp;")
                              .Replace("\r", string.Empty)
                              .Replace("\n", "<br />");

                switch (diff.operation)
                {
                    case Operation.INSERT:
                        numBytesInserted += (ulong)diff.text.Length;
                        html.Append("<ins style=\"background:#e6ffe6;\">").Append(text).Append("</ins>");
                        break;
                    case Operation.DELETE:
                        numBytesDeleted += (ulong)diff.text.Length;
                        html.Append("<del style=\"background:#ffe6e6;\">").Append(text).Append("</del>");
                        break;
                    case Operation.EQUAL:
                        numBytesEqual += (ulong)diff.text.Length;
                        html.Append("<span>").Append(text).Append("</span>");
                        break;
                }
            }

            ulong numBytesOld = numBytesEqual + numBytesDeleted;
            ulong numBytesNew = numBytesEqual + numBytesInserted;

            double percentChangeOld = ((double)numBytesDeleted / (double)numBytesOld) * 100;
            double percentChangeNew = ((double)numBytesInserted / (double)numBytesNew) * 100;
            double percentChangeAverage = (percentChangeOld + percentChangeNew) / 2;

            cs.PercentChanged = percentChangeAverage;
            cs.NumBytesDeleted = numBytesDeleted;
            cs.NumBytesInserted = numBytesInserted;
            cs.NumBytesOld = numBytesOld;
            cs.NumBytesNew = numBytesNew;

            StringBuilder output = new StringBuilder();
            output.Append(
                "percentage change: " +
                string.Format("{0:0.0000}%", percentChangeAverage) +
                "<br /><br />");
            output.Append(RemoveUnchangedLines(html.ToString()));

            return output.ToString();
        }

        /// <summary>
        /// Remove lines that were unchanged in the diff. Optionally state how many lines above and below differences
        /// will be shown.
        /// </summary>
        /// <param name="html">The html representation of the diffs.</param>
        /// <param name="numLinesBefore">The number of equal lines above each difference to be shown.</param>
        /// <param name="numLinesAfter">The number of equal lines below each difference to be shown.</param>
        /// <returns>the reduced HTML representation of the diffs</returns>
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

        /// <summary>
        /// Represents a line that has been processed. This is an intermediary object used for minimizing which line
        /// differences will be shown in the final output.
        /// </summary>
        private class DiffedLine
        {
            /// <summary>
            /// Gets or sets a value indicating whether this line will be shown.
            /// </summary>
            public bool Show { get; set; }

            /// <summary>
            /// Gets or sets a value indicating whether an insertion or deletion occurred on this line.
            /// </summary>
            public bool IsChange { get; set; }

            /// <summary>
            /// Gets or sets the actual line of text.
            /// </summary>
            public string Text { get; set; }

            /// <summary>
            /// Gets or sets the line number of this text.
            /// </summary>
            public int LineNumber { get; set; }
        }

        /// <summary>
        /// Statistics about the changes that have taken place within a single file.
        /// </summary>
        private class ChangeStats
        {
            /// <summary>
            /// Gets a hash of this relative path. Used for unique identification purposes.
            /// </summary>
            public string PathHash
            {
                get
                {
                    return GetMD5OfString(this.RelativePath);
                }
            }

            /// <summary>
            /// Gets or sets the relative path to this file.
            /// </summary>
            public string RelativePath { get; set; }

            /// <summary>
            /// Gets or sets the percentage change of the file from the old to the new version.
            /// </summary>
            public double PercentChanged { get; set; }

            /// <summary>
            /// Gets or sets the number of bytes inserted into the file.
            /// </summary>
            public ulong NumBytesInserted { get; set; }

            /// <summary>
            /// Gets or sets the number of bytes deleted from the file.
            /// </summary>
            public ulong NumBytesDeleted { get; set; }

            /// <summary>
            /// Gets or sets the number of bytes within the original file.
            /// </summary>
            public ulong NumBytesOld { get; set; }

            /// <summary>
            /// Gets or sets the number of bytes within the patched file.
            /// </summary>
            public ulong NumBytesNew { get; set; }
        }
    }
}
