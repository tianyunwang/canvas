﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NDesk.Options;

namespace CanvasPartition
{
    class CanvasPartition
    {
        static void ShowHelp(OptionSet p)
        {
            Console.WriteLine("Usage: CanvasPartition.exe [OPTIONS]+");
            Console.WriteLine("Divide bins into consistent intervals based on their counts");
            Console.WriteLine();
            Console.WriteLine("Options:");
            p.WriteOptionDescriptions(Console.Out);
        }

        static int Main(string[] args)
        {
            CanvasCommon.Utilities.LogCommandLine(args);
            string inFile = null;
            List<string> outFiles = new List<string>();
            bool needHelp = false;
            bool isGermline = false;
            string bedPath = null;
            string commonCNVsbedPath = null;
            double alpha = CBSRunner.DefaultAlpha;
            double madFactor = WaveletsRunner.DefaultMadFactor;
            SegmentSplitUndo undoMethod = SegmentSplitUndo.None;
            Segmentation.SegmentationMethod partitionMethod = Segmentation.SegmentationMethod.HMM;
            int maxInterBinDistInSegment = 1000000;
            OptionSet p = new OptionSet()
            {
                { "i|infile=", "input file - usually generated by CanvasClean", v => inFile = v },
                { "o|outfile=", "text file to output", v => outFiles.Add(v) },
                { "h|help", "show this message and exit", v => needHelp = v != null },
                { "m|method=", "segmentation method (Wavelets/CBS). Default: " + partitionMethod, v => partitionMethod = (Segmentation.SegmentationMethod)Enum.Parse(typeof(Segmentation.SegmentationMethod), v) },
                { "a|alpha=", "alpha parameter to CBS. Default: " + alpha, v => alpha = float.Parse(v) },
                { "s|split=", "CBS split method (None/Prune/SDUndo). Default: " + undoMethod, v => undoMethod = (SegmentSplitUndo)Enum.Parse(typeof(SegmentSplitUndo), v) },
                { "f|madFactor=", "MAD factor to Wavelets. Default: " + madFactor, v => madFactor = float.Parse(v) },
                { "b|bedfile=", "bed file to exclude (don't span these intervals)", v => bedPath = v },
                { "c|commoncnvs=", "bed file with common CNVs (always include these intervals into segmentation results)", v => commonCNVsbedPath = v },             
                { "g|germline", "flag indicating that input file represents germline genome", v => isGermline = v != null },
                { "d|maxInterBinDistInSegment=", "the maximum distance between adjacent bins in a segment (negative numbers turn off splitting segments after segmentation). Default: " + maxInterBinDistInSegment, v => maxInterBinDistInSegment = int.Parse(v) },
            };

            List<string> extraArgs = p.Parse(args);

            if (needHelp)
            {
                ShowHelp(p);
                return 0;
            }

            if (inFile == null || outFiles.Count == 0)
            {
                ShowHelp(p);
                return 0;
            }

            if (!File.Exists(inFile))
            {
                Console.WriteLine("CanvasPartition.exe: File {0} does not exist! Exiting.", inFile);
                return 1;
            }

            if (!string.IsNullOrEmpty(bedPath) && !File.Exists(bedPath))
            {
                Console.WriteLine("CanvasPartition.exe: File {0} does not exist! Exiting.", bedPath);
                return 1;
            }

            if (partitionMethod != Segmentation.SegmentationMethod.HMM && outFiles.Count > 1)
            {
                Console.WriteLine("CanvasPartition.exe: SegmentationMethod.HMM only works for MultiSample SPW worlfow, " +
                                  "please provide multiple -o arguments");
                return 1;
            }

            Segmentation segmentationEngine = partitionMethod == Segmentation.SegmentationMethod.HMM ?
                 new Segmentation(inFile, bedPath, outFiles.Count): 
                 new Segmentation(inFile, bedPath, maxInterBinDistInSegment: maxInterBinDistInSegment);

            Segmentation.GenomeSegmentationResults segmentationResults;
            switch (partitionMethod)
            {
                default:// use Wavelets if CBS is not selected       
                    Console.WriteLine("{0} Running Wavelet Partitioning", DateTime.Now);
                    WaveletsRunner waveletsRunner = new WaveletsRunner(new WaveletsRunner.WaveletsRunnerParams(isGermline, commonCNVsbedPath, madFactor: madFactor, verbose: 2));
                    segmentationResults = new Segmentation.GenomeSegmentationResults(waveletsRunner.Run(segmentationEngine));
                    segmentationEngine.WriteCanvasPartitionResults(outFiles.Single(), segmentationResults);
                    break;
                case Segmentation.SegmentationMethod.CBS:
                    Console.WriteLine("{0} Running CBS Partitioning", DateTime.Now);
                    CBSRunner cbsRunner = new CBSRunner(maxInterBinDistInSegment, undoMethod, alpha);
                    segmentationResults = new Segmentation.GenomeSegmentationResults(cbsRunner.Run(segmentationEngine, verbose: 2));
                    segmentationEngine.WriteCanvasPartitionResults(outFiles.First(), segmentationResults);
                    break;
                case Segmentation.SegmentationMethod.HMM:
                    Console.WriteLine("{0} Running HMM Partitioning", DateTime.Now);
                    HiddenMarkovModelsRunner hiddenMarkovModelsRunner = new HiddenMarkovModelsRunner(commonCNVsbedPath);
                    segmentationResults = new Segmentation.GenomeSegmentationResults(hiddenMarkovModelsRunner.Run(segmentationEngine));
                    segmentationEngine.WriteCanvasPartitionMultisampleResults(outFiles, segmentationResults);
                    break;
            }
            Console.WriteLine("{0} CanvasPartition results written out", DateTime.Now);
            return 0;
        }
    }
}
