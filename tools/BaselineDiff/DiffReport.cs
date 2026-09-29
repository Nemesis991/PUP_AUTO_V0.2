namespace BaselineDiff
{
    /// <summary>Collects and prints differences; the count drives the exit code.</summary>
    public sealed class DiffReport
    {
        private readonly TextWriter _out;

        public DiffReport(TextWriter output) => _out = output;

        public int Count { get; private set; }

        public void Add(string where, string before, string after)
        {
            Count++;
            _out.WriteLine(where);
            _out.WriteLine($"  before: {before}");
            _out.WriteLine($"  after:  {after}");
        }

        public void Note(string message) => _out.WriteLine(message);
    }
}
