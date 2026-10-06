using System.Reflection;

namespace WatchSentence;

public sealed record Quote(int Minute, string Text, string Phrase, string Title, string Author);

/// <summary>Literary quotes indexed by minute of the day (0..1439).</summary>
public sealed class QuoteBook
{
    private readonly List<Quote>[] _byMinute = new List<Quote>[1440];

    public int Count { get; }

    public QuoteBook()
    {
        for (int i = 0; i < _byMinute.Length; i++) _byMinute[i] = new List<Quote>();

        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("quotes.tsv")
            ?? throw new InvalidOperationException("quotes.tsv resource missing");
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Length == 0 || line[0] == '#') continue;
            var f = line.Split('\t');
            if (f.Length < 5 || f[0].Length != 5) continue;
            int minute = int.Parse(f[0][..2]) * 60 + int.Parse(f[0][3..]);
            _byMinute[minute].Add(new Quote(minute, f[1], f[2], f[3], f[4]));
            Count++;
        }
    }

    /// <summary>
    /// A quote for this exact minute if one exists, otherwise the closest earlier minute
    /// that has one (so 4:32 falls back to "half past four").
    /// <paramref name="pick"/> chooses among several candidates for the same minute.
    /// </summary>
    public Quote? For(int hour, int minute, int pick)
    {
        int m = hour * 60 + minute;
        for (int back = 0; back < 1440; back++)
        {
            var list = _byMinute[((m - back) % 1440 + 1440) % 1440];
            if (list.Count > 0) return list[(pick & int.MaxValue) % list.Count];
        }
        return null;
    }

    public bool IsExact(int hour, int minute) => _byMinute[hour * 60 + minute].Count > 0;
}
