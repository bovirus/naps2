﻿namespace NAPS2.Util;

public static class NumberExtensions
{
    /// <summary>
    /// Ensures the provided value is within the provided range (inclusive).
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="val"></param>
    /// <param name="min"></param>
    /// <param name="max"></param>
    /// <returns></returns>
    public static T Clamp<T>(this T val, T min, T max) where T : IComparable<T>
    {
        if (val.CompareTo(min) < 0)
        {
            return min;
        }
        if (val.CompareTo(max) > 0)
        {
            return max;
        }
        return val;
    }

    /// <summary>
    /// Ensures the provided value is at least the provided minimum value (inclusive).
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="val"></param>
    /// <param name="min"></param>
    /// <returns></returns>
    public static T AtLeast<T>(this T val, T min) where T : IComparable<T>
    {
        if (val.CompareTo(min) < 0)
        {
            return min;
        }
        return val;
    }

    /// <summary>
    /// Ensures the provided value is at most the provided maximum value (inclusive).
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="val"></param>
    /// <param name="max"></param>
    /// <returns></returns>
    public static T AtMost<T>(this T val, T max) where T : IComparable<T>
    {
        if (val.CompareTo(max) > 0)
        {
            return max;
        }
        return val;
    }
}