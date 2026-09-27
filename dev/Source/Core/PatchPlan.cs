using System;
using System.Collections.Generic;

namespace MapGenAI
{
    /// <summary>
    /// Applies the mod's game patches one class at a time.
    /// Harmony's PatchAll stops at the first class that throws, and the world-map button patch is the last class
    /// in the assembly, so one patch that failed in a player's setup used to remove the button and the mod
    /// settings page as well. Here the entry patch goes first and a failing class only turns off its own feature.
    /// </summary>
    public static class PatchPlan
    {
        public sealed class Result
        {
            public readonly List<string> Applied = new List<string>();
            public readonly List<string> Failed = new List<string>();
        }

        /// <summary>Items marked first come before all others. Both groups keep their original order.</summary>
        public static List<T> Order<T>(IEnumerable<T> items, Func<T, bool> isFirst)
        {
            var first = new List<T>();
            var rest = new List<T>();
            foreach (var item in items) (isFirst(item) ? first : rest).Add(item);
            first.AddRange(rest);
            return first;
        }

        public static Result Apply<T>(IEnumerable<T> items, Func<T, bool> isFirst, Func<T, string> name, Action<T> patch)
        {
            var result = new Result();
            foreach (var item in Order(items, isFirst))
            {
                try
                {
                    patch(item);
                    result.Applied.Add(name(item));
                }
                catch (Exception e)
                {
                    result.Failed.Add(name(item) + ": " + Describe(e));
                }
            }
            return result;
        }

        /// <summary>Harmony wraps the real cause, so report the innermost exception.</summary>
        public static string Describe(Exception e)
        {
            var root = e;
            while (root.InnerException != null) root = root.InnerException;
            return root.GetType().Name + ": " + root.Message;
        }
    }
}
