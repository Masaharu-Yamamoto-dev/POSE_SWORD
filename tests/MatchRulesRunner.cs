using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

// Runs the same pure rule tests as Unity EditMode without starting the Editor.
// Deliberately supports only parameterless [Test] methods; no Unity lifecycle is simulated.
public static class MatchRulesRunner
{
    public static int Main()
    {
        int failures = 0;
        var methods = typeof(MatchRulesTests).GetMethods()
            .Where(m => m.IsDefined(typeof(TestAttribute), false)).ToArray();
        foreach (var method in methods)
        {
            try
            {
                method.Invoke(new MatchRulesTests(), null);
                Console.WriteLine("PASS " + method.Name);
            }
            catch (Exception e)
            {
                failures++;
                Console.WriteLine("FAIL " + method.Name + ": " + (e.InnerException ?? e));
            }
        }
        Console.WriteLine((methods.Length - failures) + "/" + methods.Length + " passed");
        return failures == 0 && methods.Length > 0 ? 0 : 1;
    }
}
