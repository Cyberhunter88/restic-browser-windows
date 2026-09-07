using System.Text.Json;
using System.Reflection;
using System.IO.Pipes;
using System.Security.Cryptography;
using ResticBrowser.Models;
using ResticBrowser.Remote;
using ResticBrowser.Services;
using ResticBrowser.ViewModels;


internal static partial class TestSuite
{
    internal static Task Sync(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    internal static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"Erwartet: {expected}; erhalten: {actual}");
    }

    internal static void True(bool value)
    {
        if (!value) throw new Exception("Bedingung ist nicht erfüllt.");
    }
}
