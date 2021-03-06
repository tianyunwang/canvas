using System.Collections.Generic;
using CanvasCommon;
using Illumina.Common.FileSystem;
using Isas.ClassicBioinfoTools.KentUtils;
using Isas.Framework.Logging;
using Isas.Framework.Utilities;
using Isas.SequencingFiles;

namespace CanvasPedigreeCaller.Visualization
{
    public class CoverageBigWigWriter : ICoverageBigWigWriter
    {
        private readonly ILogger _logger;
        private readonly CoverageBedGraphWriter _writer;
        private readonly IBedGraphToBigWigConverter _converter;
        private readonly GenomeMetadata _genome;

        public CoverageBigWigWriter(ILogger logger, CoverageBedGraphWriter writer, IBedGraphToBigWigConverter converter, GenomeMetadata genome)
        {
            _logger = logger;
            _writer = writer;
            _converter = converter;
            _genome = genome;
        }

        public IFileLocation Write(IReadOnlyList<CanvasSegment> segments, IDirectoryLocation output,
            double normalizationFactor)
        {
            _logger.Info($"Begin writing bedgraph file at '{output}'");
            var benchmark = new Benchmark();
            var bedGraph = output.GetFileLocation("coverage.bedgraph");
            _writer.Write(segments, bedGraph, normalizationFactor);
            _logger.Info($"Finished writing bedgraph file at '{bedGraph}'. Elapsed time: {benchmark.GetElapsedTime()}");
            _logger.Info($"Begin conversion of '{bedGraph}' to bigwig file");
            benchmark = new Benchmark();
            var bigWigConverterOutput = output.CreateSubdirectory("BigWigConverter");
            var bigwigFile = _converter.Convert(bedGraph, _genome, bigWigConverterOutput);
            if (bigwigFile != null)
            {
                _logger.Info(
                    $"Finished conversion from bedgraph file at '{bedGraph}' to bigwig file at '{bigwigFile}'. Elapsed time: {benchmark.GetElapsedTime()}");
            }
            return bigwigFile;
        }
    }
}