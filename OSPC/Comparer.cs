﻿using OSPC.Tokenizer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OSPC
{
    public class Match
    {
        public Match(CompareResult parent)
        {
            this.Result = parent;
            this.TokensA = new LinkedList<Token>();
            this.TokensB = new LinkedList<Token>();
        }

        public CompareResult Result { get; private set; }
        public LinkedList<Token> TokensA { get; private set; }
        public LinkedList<Token> TokensB { get; private set; }
        public int Index { get; set; }

        public override string ToString()
        {
            return string.Format("# {0}: {1}", Index, string.Join(" ", TokensA.Select(t => t.Text)));
        }
    }

    public class CompareResult
    {
        public CompareResult(Submission a, Submission b)
        {
            this.A = a;
            this.B = b;
            Matches = new List<Match>();
        }
        public Submission A { get; private set; }
        public Submission B { get; private set; }

        public int Index { get; private set; }
        public List<Match> Matches { get; private set; }

        public void Seal(int index)
        {
            Index = index;
        }

        public void Update()
        {
            MatchCount = Matches.Count;
            TokenCount = Matches.Sum(m => m.TokensA.Count);
            SimilarityA = (double)TokenCount / (double)A.Tokens.Length;
            SimilarityB = (double)TokenCount / (double)B.Tokens.Length;
        }

        public int MatchCount { get; private set; }
        public int TokenCount { get; private set; }
        public double SimilarityA { get; private set; }
        public double SimilarityB { get; private set; }
    }

    public class Comparer
    {
        private readonly Configuration _cfg;

        public Comparer(Configuration cfg)
        {
            this._cfg = cfg;
        }

        public List<CompareResult> Compare(Submission[] files)
        {
            var compareList = new List<Tuple<Submission, Submission>>();
            for (int a = 0; a < files.Length; a++)
            {
                for (int b = a + 1; b < files.Length; b++)
                {
                    if (Path.GetExtension(files[a].FilePath) != Path.GetExtension(files[b].FilePath)) continue;

                    compareList.Add(new Tuple<Submission, Submission>(files[a], files[b]));
                }
            }

            int progressCounter = 0;
            object _lock = new object();
            var compareResult = new List<CompareResult>();

#if SINGLE_THREADED
            foreach(var pair in compareList)
#else
            Parallel.ForEach(compareList, pair =>
#endif
            {
                var r = this.Compare(pair.Item1, pair.Item2);

                lock (_lock)
                {
                    compareResult.Add(r);
                    if (++progressCounter % 100 == 0) Console.Write(".");
                }
            }
#if !SINGLE_THREADED
            );
#endif
            Console.WriteLine();

            return compareResult;
        }

        public CompareResult Compare(Submission a, Submission b)
        {
            CompareResult result = new CompareResult(a, b);
            var matches = new List<Match>();
            // reduce access to properties
            var a_length = a.Tokens.Length;
            var b_length = b.Tokens.Length;
            var a_tokens = a.Tokens;
            var b_tokens = b.Tokens;

            for (int idx_a = 0; idx_a < a_length; idx_a++)
            {
                int idx_working_a = idx_a;
                int inMatch = 0;
                Match currentMatch = null;

                for (int idx_working_b = 0; idx_working_b < b_length; idx_working_b++)
                {
                    var working_a = a_tokens[idx_working_a];
                    var working_b = b_tokens[idx_working_b];

                    // reduce access to properties
                    var a_text = working_a.Text;
                    var b_text = working_b.Text;

                    if (a_text == b_text)
                    {
                        currentMatch = ProcessMatch(working_a, working_b, currentMatch, result);

                        idx_working_a++;
                        if (idx_working_a >= a_length) break;

                        inMatch = 1;
                    }
                    else if (inMatch > 0 && a_text != b_text)
                    {
                        if (inMatch >= _cfg.MAX_MATCH_DISTANCE)
                        {
                            FinishMatch(currentMatch, matches);
                            currentMatch = null;
                            inMatch = 0;
                        }
                        else
                        {
                            currentMatch = ProcessMatch(working_a, working_b, currentMatch, result);

                            idx_working_a++;
                            if (idx_working_a >= a_length) break;

                            inMatch++;
                        }
                    }
                } // foreach(b)
                FinishMatch(currentMatch, matches);
            } // foreach(a)

            // Find longest match
            foreach (var match in matches.OrderByDescending(i => i.TokensA.Count))
            {
                if (!result.Matches.Any(m => m.TokensA.Any(t => match.TokensA.Contains(t))
                                          || m.TokensB.Any(t => match.TokensB.Contains(t))))
                {
                    match.Index = result.Matches.Count;
                    result.Matches.Add(match);
                }
            }
            result.Update();
            return result;
        }

        private Match ProcessMatch(Token working_a, Token working_b, Match currentMatch, CompareResult result)
        {
            if (currentMatch == null)
            {
                currentMatch = new Match(result);
            }
            currentMatch.TokensA.AddLast(working_a);
            currentMatch.TokensB.AddLast(working_b);
            return currentMatch;
        }

        private Match FinishMatch(Match currentMatch, List<Match> matches)
        {
            if (currentMatch == null) return null;

            var a_text = currentMatch.TokensA.Select(a => a.Text).Distinct().ToArray();
            var b_text = currentMatch.TokensB.Select(a => a.Text).Distinct().ToArray();

            var allMatches = a_text.Length;
            var realMatches = a_text.Intersect(b_text).Count();
            var p = (double)realMatches / (double)allMatches;

            if (allMatches >= _cfg.MIN_MATCH_LENGTH && p >= _cfg.MIN_COMMON_TOKEN)
            {
                matches.Add(currentMatch);
            }
            return null;
        }
    }
}