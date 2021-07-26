

using System;
using UnityEditor;
using UnityEngine;
using System.Diagnostics;
using Debug = UnityEngine.Debug;
using System.Collections.Generic;

static class Tester
{
    [MenuItem("MyMenu/RunTests")]
    static void Run()
    {
        Test1();
        Test2();
        Test3();
    }

    static void Test1()
    {
        // add a million consecutive, and their squares
        var dict = new RobinHoodDictionary<int, int>();
        var dict2 = new Dictionary<int, int>();
        Stopwatch sw = new Stopwatch();
        Stopwatch sw2 = new Stopwatch();

        sw.Start();
        for (int x = 0; x < 1000000; x++)
            dict.Add(x, x);

        for (int x = 0; x < 1000000; x++)
            if ((dict[x] - x) != 0)
                Debug.Log($"Test1 {x} is incorrect");
        sw.Stop();

        sw2.Start();
        for (int x = 0; x < 1000000; x++)
            dict2.Add(x, x);

        for (int x = 0; x < 1000000; x++)
            if ((dict2[x] - x) != 0)
                Debug.Log($"Test1.2 {x} is incorrect");
        sw2.Stop();

        Debug.Log($"Test1 Complete!  Elapsed={sw.Elapsed} vs {sw2.Elapsed}");
    }

    static void Test2()
    {
        // add a million consecutive, and their squares
        var dict = new RobinHoodDictionary<int, int>();
        for (int x = 0; x < 10000; x++)
            dict.Add((Int32)(x*x), x);

        for (int x = 0; x < 10000; x++)
            if ((dict[(Int32)(x * x)] - x) != 0)
                Debug.Log($"Test2 {x} is incorrect");

        Debug.Log("Test2 Complete!");
    }

    static void Test3()
    {
        // add a million consecutive, and their squares
        var dict = new RobinHoodDictionary<int, int>();
        for (int x = 0; x < 1000000; x++)
            dict.Add(x * 5, x);

        for (int x = 0; x < 1000000; x++)
            if ((dict[x * 5] - x) != 0)
                Debug.Log($"Test3 {x} is incorrect");

        Debug.Log("Test3 Complete!");
    }
}