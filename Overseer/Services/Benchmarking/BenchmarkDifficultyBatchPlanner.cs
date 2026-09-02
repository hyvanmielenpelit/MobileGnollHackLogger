namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;

public static class BenchmarkDifficultyBatchPlanner
{
    public static IReadOnlyList<IReadOnlyList<T>> Plan<T>(IReadOnlyList<T> items, int batchSize)
    {
        if (items == null || items.Count == 0)
        {
            return Array.Empty<IReadOnlyList<T>>();
        }

        int clampedBatchSize = Math.Clamp(batchSize, 1, 25);
        var batches = new List<IReadOnlyList<T>>();

        for (int i = 0; i < items.Count; i += clampedBatchSize)
        {
            int count = Math.Min(clampedBatchSize, items.Count - i);
            var batch = new List<T>(count);
            for (int j = 0; j < count; j++)
            {
                batch.Add(items[i + j]);
            }
            batches.Add(batch);
        }

        return batches;
    }

    public static IReadOnlyList<IReadOnlyList<T>> Split<T>(IReadOnlyList<T> batch)
    {
        if (batch == null || batch.Count <= 1)
        {
            return Array.Empty<IReadOnlyList<T>>();
        }

        int half = (batch.Count + 1) / 2; // Ceil division: e.g. 3 -> 2 and 1; 4 -> 2 and 2
        var firstHalf = new List<T>(half);
        var secondHalf = new List<T>(batch.Count - half);

        for (int i = 0; i < half; i++)
        {
            firstHalf.Add(batch[i]);
        }

        for (int i = half; i < batch.Count; i++)
        {
            secondHalf.Add(batch[i]);
        }

        return new List<IReadOnlyList<T>> { firstHalf, secondHalf };
    }
}
