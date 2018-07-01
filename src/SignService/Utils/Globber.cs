using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

// "Borrowed" from https://github.com/Wyamio/Wyam/blob/c7a26da931477a48006f6ebc574c074458719774/src/core/Wyam.Core/IO/Globbing/Globber.cs
// Copyright (c) 2017 Dave Glick
namespace Wyam.Core.IO.Globbing
{
    /// <summary>
    /// Helper methods to work with globbing patterns.
    /// </summary>
    public static class Globber
    {
        static readonly Regex HasBraces = new Regex(@"\{.*\}");
        static readonly Regex NumericSet = new Regex(@"^\{(-?[0-9]+)\.\.(-?[0-9]+)\}");

        /// <summary>
        /// Gets files from the specified directory using globbing patterns.
        /// </summary>
        /// <param name="directory">The directory to search.</param>
        /// <param name="patterns">The globbing pattern(s) to use.</param>
        /// <returns>Files that match the globbing pattern(s).</returns>
        public static IEnumerable<FileInfo> GetFiles(DirectoryInfo directory, params string[] patterns) =>
            GetFiles(directory, (IEnumerable<string>)patterns);

        /// <summary>
        /// Gets files from the specified directory using globbing patterns.
        /// </summary>
        /// <param name="directory">The directory to search.</param>
        /// <param name="patterns">The globbing pattern(s) to use.</param>
        /// <returns>Files that match the globbing pattern(s).</returns>
        public static IEnumerable<FileInfo> GetFiles(DirectoryInfo directory, IEnumerable<string> patterns)
        {
            // Initially based on code from Reliak.FileSystemGlobbingExtensions (https://github.com/reliak/Reliak.FileSystemGlobbingExtensions)

            var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);

            // Expand braces
            var expandedPatterns = patterns
                .SelectMany(ExpandBraces)
                .Select(f => f.Replace("\\{", "{").Replace("\\}", "}")); // Unescape braces

            // Add the patterns, any that start with ! are exclusions
            foreach (var expandedPattern in expandedPatterns)
            {
                var isExclude = expandedPattern[0] == '!';
                var finalPattern = isExclude ? expandedPattern.Substring(1) : expandedPattern;
                finalPattern = finalPattern
                    .Replace("\\!", "!") // Unescape negation
                    .Replace("\\", "/"); // Normalize slashes

                // No support for absolute paths
                if (System.IO.Path.IsPathRooted(finalPattern))
                {
                    throw new ArgumentException($"Rooted globbing patterns are not supported ({expandedPattern})", nameof(patterns));
                }

                // Add exclude or include pattern to matcher
                if (isExclude)
                {
                    matcher.AddExclude(finalPattern);
                }
                else
                {
                    matcher.AddInclude(finalPattern);
                }
            }

            DirectoryInfoBase directoryInfo = new DirectoryInfoWrapper(directory);
            var result = matcher.Execute(directoryInfo);
            return result.Files.Select(match => directoryInfo.GetFile(match.Path)).Select(fb => new FileInfo(fb.FullName));
        }

        /// <summary>Expands all brace ranges in a pattern, returning a sequence containing every possible combination.</summary>
        /// <param name="pattern">The pattern to expand.</param>
        /// <returns>The expanded globbing patterns.</returns>
        public static IEnumerable<string> ExpandBraces(string pattern)
        {
            // Initially based on code from Minimatch (https://github.com/SLaks/Minimatch/blob/master/Minimatch/Minimatcher.cs)
            // Brace expansion:
            // a{b,c}d -> abd acd
            // a{b,}c -> abc ac
            // a{0..3}d -> a0d a1d a2d a3d
            // a{b,c{d,e}f}g -> abg acdfg acefg
            // a{b,c}d{e,f}g -> abdeg acdeg abdeg abdfg
            //
            // Invalid sets are not expanded.
            // a{2..}b -> a{2..}b
            // a{b}c -> a{b}c

            if (!HasBraces.IsMatch(pattern))
            {
                // shortcut. no need to expand.
                return new[] { pattern };
            }

            var escaping = false;
            int i;

            // examples and comments refer to this crazy pattern:
            // a{b,c{d,e},{f,g}h}x{y,z}
            // expected:
            // abxy
            // abxz
            // acdxy
            // acdxz
            // acexy
            // acexz
            // afhxy
            // afhxz
            // aghxy
            // aghxz

            // everything before the first \{ is just a prefix.
            // So, we pluck that off, and work with the rest,
            // and then prepend it to everything we find.
            if (pattern[0] != '{')
            {
                string prefix = null;
                for (i = 0; i < pattern.Length; i++)
                {
                    var c = pattern[i];
                    if (c == '\\')
                    {
                        escaping = !escaping;
                    }
                    else if (c == '{' && !escaping)
                    {
                        prefix = pattern.Substring(0, i);
                        break;
                    }
                    else
                    {
                        escaping = false;
                    }
                }

                // actually no sets, all { were escaped.
                if (prefix == null)
                {
                    // no sets
                    return new[] { pattern };
                }

                return ExpandBraces(pattern.Substring(i)).Select(t =>
                {
                    var neg = string.Empty;

                    // Check for negated subpattern
                    if (t.Length > 0 && t[0] == '!')
                    {
                        if (prefix[0] != '!')
                        {
                            // Only add a new negation if there isn't already one
                            neg = "!";
                        }
                        t = t.Substring(1);
                    }

                    // Remove duplicated path separators (can happen when there's an empty expansion like "baz/{foo,}/bar")
                    if (t.Length > 0 && t[0] == '/' && prefix[prefix.Length - 1] == '/')
                    {
                        t = t.Substring(1);
                    }

                    return neg + prefix + t;
                });
            }

            // now we have something like:
            // {b,c{d,e},{f,g}h}x{y,z}
            // walk through the set, expanding each part, until
            // the set ends.  then, we'll expand the suffix.
            // If the set only has a single member, then'll put the {} back

            // first, handle numeric sets, since they're easier
            var numset = NumericSet.Match(pattern);
            if (numset.Success)
            {
                // console.error("numset", numset[1], numset[2])
                var suf = ExpandBraces(pattern.Substring(numset.Length)).ToList();
                int start = int.Parse(numset.Groups[1].Value),
                end = int.Parse(numset.Groups[2].Value),
                inc = start > end ? -1 : 1;
                var retVal = new List<string>();
                for (var w = start; w != (end + inc); w += inc)
                {
                    // append all the suffixes
                    retVal.AddRange(suf.Select(t => w + t));
                }
                return retVal;
            }

            // ok, walk through the set
            // We hope, somewhat optimistically, that there
            // will be a } at the end.
            // If the closing brace isn't found, then the pattern is
            // interpreted as braceExpand("\\" + pattern) so that
            // the leading \{ will be interpreted literally.
            var depth = 1;
            var set = new List<string>();
            var member = string.Empty;
            escaping = false;

            for (i = 1 /* skip the \{ */; i < pattern.Length && depth > 0; i++)
            {
                var c = pattern[i];

                if (escaping)
                {
                    escaping = false;
                    member += "\\" + c;
                }
                else
                {
                    switch (c)
                    {
                        case '\\':
                            escaping = true;
                            continue;

                        case '{':
                            depth++;
                            member += "{";
                            continue;

                        case '}':
                            depth--;

                            // if this closes the actual set, then we're done
                            if (depth == 0)
                            {
                                set.Add(member);
                                member = string.Empty;

                                // pluck off the close-brace
                                break;
                            }
                            else
                            {
                                member += c;
                                continue;
                            }

                        case ',':
                            if (depth == 1)
                            {
                                set.Add(member);
                                member = string.Empty;
                            }
                            else
                            {
                                member += c;
                            }
                            continue;

                        default:
                            member += c;
                            continue;
                    } // switch
                } // else
            } // for

            // now we've either finished the set, and the suffix is
            // pattern.substr(i), or we have *not* closed the set,
            // and need to escape the leading brace
            if (depth != 0)
            {
                // didn't close pattern
                return ExpandBraces("\\" + pattern);
            }

            // ["b", "c{d,e}","{f,g}h"] -> ["b", "cd", "ce", "fh", "gh"]
            var addBraces = set.Count == 1;

            set = set.SelectMany(ExpandBraces).ToList();

            if (addBraces)
            {
                set = set.Select(s => "{" + s + "}").ToList();
            }

            // now attach the suffixes.
            // x{y,z} -> ["xy", "xz"]
            // console.error("set", set)
            // console.error("suffix", pattern.substr(i))
            return ExpandBraces(pattern.Substring(i)).SelectMany(suf =>
            {
                var negated = false;
                if (suf.Length > 0 && suf[0] == '!')
                {
                    negated = true;
                    suf = suf.Substring(1);
                }
                return set.Select(s =>
                {
                    var neg = string.Empty;
                    if (negated && (s.Length == 0 || s[0] != '!'))
                    {
                        // Only add a new negation if there isn't already one
                        neg = "!";
                    }
                    return neg + s + suf;
                });
            });
        }
    }
}