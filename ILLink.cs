namespace illinkrepro
{
    internal class ILLink
    {
        public abstract record Argument();

        public record UnknownArgument(string Value) : Argument
        {
            public override string ToString() => Value;
        }

        private record DotnetPathSection(string Path) : Argument
        {
            public override string ToString() => Path;
        }

        private record ILLinkPathSection(string Path) : Argument
        {
            public override string ToString() => Path;
        }

        public record Root(string AssemblyPath, string? Mode) : Argument
        {
            public override string ToString() => $"-a {AssemblyPath} {Mode}";
        }

        public record Reference(string AssemblyPath) : Argument
        {
            public override string ToString() => $"-reference {AssemblyPath}";
        }

        public record Out(string Path) : Argument
        {
            public override string ToString() => $"-out {Path}";
        }

        public record Descriptor(string Path) : Argument
        {
            public override string ToString() => $"-x {Path}";
        }

        public record LinkAttributes(string Path) : Argument
        {
            public override string ToString() => $"--link-attributes {Path}";
        }

        public record SearchDirectory(string Path) : Argument
        {
            public override string ToString() => $"-d {Path}";
        }

        readonly string _workingPath;

        public string DotnetPath { get; private set; }
        public string ILLinkPath { get; private set; }
        public Argument[] Arguments { get; }

        public ILLink(string workingPath, string commandLine)
        {
            _workingPath = workingPath;
            Arguments = Parse(commandLine).ToArray();

            if (DotnetPath == null || ILLinkPath == null)
                throw new ApplicationException("Invalid ILLink command line");
        }


        IEnumerable<Argument> Parse(string commandLine)
        {
            var lines = commandLine.Split(new[] { '\n', '\r' }).Select(l => l.Trim(new[] { '\n', '\r' })).ToList();

            var firstLine = lines.FirstOrDefault();
            if (firstLine == null)
                throw new ApplicationException("Invalid ILLink command line detected");

            int firstQuote = firstLine.IndexOf('"');
            DotnetPath = firstLine[..firstQuote].Trim(' ');

            var firstLineSplit = SplitLine(firstLine[firstQuote..]).ToArray();
            if (firstLineSplit.Length < 1)
                throw new ApplicationException("Invalid ILLink command line detected");

            ILLinkPath = ToPath(firstLineSplit.First());

            lines[0] = string.Join(" ", firstLineSplit.Skip(1));

            List<string[]> splitLines = new List<string[]>();
            foreach (var line in lines)
            {
                var lineSplit = SplitLine(line).ToArray();
                if (lineSplit.Length == 0)
                    continue;

                for (int partIndex = 0; partIndex < lineSplit.Length; partIndex++)
                {
                    if (lineSplit[partIndex].StartsWith("-"))
                    {
                        List<string> subparts = new List<string>();
                        subparts.Add(lineSplit[partIndex]);
                        int subpartIndex;
                        for (subpartIndex = partIndex + 1; subpartIndex < lineSplit.Length; subpartIndex++)
                        {
                            if (lineSplit[subpartIndex].StartsWith("-"))
                            {
                                break;
                            }

                            subparts.Add(lineSplit[subpartIndex]);
                        }

                        partIndex = subpartIndex - 1;
                        splitLines.Add(subparts.ToArray());
                    }
                    else
                    {
                        splitLines.Add(new string[] { lineSplit[partIndex] });
                    }
                }
            }

            foreach (var lineParts in splitLines)
            {
                switch (lineParts[0])
                {
                    case "-a":
                        yield return new Root(ToPath(lineParts[1]), lineParts.Length > 2 ? lineParts[2] : null); break;
                    case "-reference":
                        yield return new Reference(ToPath(lineParts[1])); break;
                    case "-out":
                        yield return new Out(ToPath(lineParts[1])); break;
                    case "-x":
                        yield return new Descriptor(ToPath(lineParts[1])); break;
                    case "--link-attributes":
                        yield return new LinkAttributes(ToPath(lineParts[1])); break;
                    case "-d":
                        yield return new SearchDirectory(ToPath(lineParts[1])); break;
                    default:
                        yield return new UnknownArgument(string.Join(' ', lineParts)); break;
                }
            }
        }

        static IEnumerable<string> SplitLine(string line)
        {
            bool quoted = false;
            int start = 0;
            for (int i = 0; i < line.Length; i++)
            {
                switch (line[i])
                {
                    case '"':
                        quoted = !quoted;
                        break;
                    case ' ':
                        if (!quoted)
                        {
                            if (start < i)
                                yield return line[start..i];
                            start = i + 1;
                        }
                        break;
                    default:
                        break;
                }
            }

            if (start < line.Length)
                yield return line[start..];
        }

        string ToPath(string v)
        {
            v = v.Trim('"');
            if (Path.IsPathRooted(v))
                return Path.GetFullPath(v);

            return Path.GetFullPath(Path.Combine(_workingPath, v));
        }
    }
}
