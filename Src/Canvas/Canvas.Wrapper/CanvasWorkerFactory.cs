﻿using System;
using System.Collections.Generic;
using System.Linq;
using Illumina.Common;
using Illumina.Common.FileSystem;
using Illumina.SecondaryAnalysis.VariantCalling;
using Isas.ClassicBioinfoTools.Tabix;
using Isas.Framework;
using Isas.Framework.FrameworkFactory;
using Isas.Framework.Logging;
using Isas.Framework.Settings;
using Isas.Framework.WorkManagement;
using Isas.SequencingFiles;

namespace Canvas.Wrapper
{
    public class CanvasWorkerFactory
    {
        private readonly IWorkManager _workManager;
        private readonly ISampleSettings _sampleSettings;
        private readonly ILogger _logger;
        private readonly ExecutableProcessor _executableProcessor;
        private readonly DbSnpVcfProcessor _dbSnpVcfProcessor;
        private readonly bool _detectCnvDefault;
        private readonly TabixWrapper _tabixWrapper;
        public static string CanvasCoverageModeSetting = "CanvasCoverageMode";

        public CanvasWorkerFactory(
            ISampleSettings sampleSettings,
            IWorkManager workManager,
            ILogger logger,
            ExecutableProcessor executableProcessor,
            DbSnpVcfProcessor dbSnpVcfProcessor,
            bool detectCnvDefault,
            TabixWrapper tabixWrapper)
        {
            _workManager = workManager;
            _sampleSettings = sampleSettings;
            _logger = logger;
            _executableProcessor = executableProcessor;
            _dbSnpVcfProcessor = dbSnpVcfProcessor;
            _detectCnvDefault = detectCnvDefault;
            _tabixWrapper = tabixWrapper;
        }
        private IFileLocation GetRuntimeExecutable()
        {
            return new FileLocation(_executableProcessor.GetEnvironmentExecutablePath("dotnet"));
        }

        public ICanvasWorker<CanvasEnrichmentInput, CanvasEnrichmentOutput> GetCanvasEnrichmentWorker()
        {
            var runtimeExecutable = GetRuntimeExecutable();
            var annotationProvider = GetAnnotationFileProvider();
            var canvasCnvCaller = new CanvasEnrichmentCnvCaller(
                _workManager,
                _logger,
                GetCanvasExe(),
                runtimeExecutable,
                annotationProvider,
                GetCanvasSingleSampleInputCommandLineBuilderWithSomaticQualityThreshold(annotationProvider),
                new CanvasEnrichmentInputCreator<CanvasEnrichmentInput>(),
                GetCanvasPloidyBedCreator());
            return GetCanvasWorker(canvasCnvCaller, CanvasEnrichmentOutput.GetFromStub);
        }

        public ICanvasWorker<CanvasResequencingInput, CanvasOutput> GetCanvasResequencingWorker(bool smallVariantCallingDisabled)
        {
            // special case: we don't do CNV calling when variant caller is disabled and we are not using a custom dbsnp vcf
            if (!CustomDbSnpVcf() && smallVariantCallingDisabled)
            {
                _logger.Info("Not running Canvas when small variant calling is disabled, unless a custom dbSNP VCF file is provided");
                if (RunCnvDetection(false))
                    throw new ArgumentException("CNV calling must be disabled when small variant calling is disabled, unless a custom dbSNP VCF file is provided");
                return new NullCanvasWorker<CanvasResequencingInput, CanvasOutput>();
            }

            var annotationProvider = GetAnnotationFileProvider();
            var runtimeExecutable = GetRuntimeExecutable();
            var canvasCnvCaller = new CanvasResequencingCnvCaller(
                _workManager,
                _logger,
                GetCanvasExe(),
                runtimeExecutable,
                annotationProvider,
                GetCanvasSingleSampleInputCommandLineBuilder(annotationProvider),
                GetCanvasPloidyBedCreator());
            return GetCanvasWorker(canvasCnvCaller, CanvasOutput.GetFromStub);
        }

        private bool CustomDbSnpVcf()
        {
            return GetDbSnpVcfPath() != null;
        }

        public ICanvasWorker<CanvasTumorNormalWgsInput, CanvasOutput> GetCanvasTumorNormalWorker()
        {
            var annotationProvider = GetAnnotationFileProvider();
            var runtimeExecutable = GetRuntimeExecutable();
            var canvasCnvCaller = new CanvasTumorNormalWgsCnvCaller(
                _workManager,
                _logger,
                GetCanvasExe(),
                runtimeExecutable,
                annotationProvider,
                GetCanvasSingleSampleInputCommandLineBuilderWithSomaticQualityThreshold(annotationProvider),
                GetCanvasPloidyBedCreator());
            return GetCanvasWorker(canvasCnvCaller, CanvasOutput.GetFromStub);
        }

        public ICanvasWorker<CanvasTumorNormalEnrichmentInput, CanvasOutput> GetCanvasTumorNormalEnrichmentWorker()
        {
            var annotationProvider = GetAnnotationFileProvider();
            var runtimeExecutable = GetRuntimeExecutable();
            var canvasCnvCaller = new CanvasTumorNormalEnrichmentCnvCaller(
                _workManager,
                _logger,
                GetCanvasExe(),
                runtimeExecutable,
                annotationProvider,
                GetCanvasSingleSampleInputCommandLineBuilderWithSomaticQualityThreshold(annotationProvider),
                new CanvasEnrichmentInputCreator<CanvasTumorNormalEnrichmentInput>(),
                GetCanvasPloidyBedCreator());
            return GetCanvasWorker(canvasCnvCaller, CanvasOutput.GetFromStub);
        }

        private CanvasPloidyBedCreator GetCanvasPloidyBedCreator()
        {
            return new CanvasPloidyBedCreator(_logger, _workManager, GetPloidyCorrector());
        }

        internal PloidyCorrector GetPloidyCorrector()
        {
            return new PloidyCorrector(_logger, _workManager, new PloidyEstimator(_logger, _workManager, _executableProcessor.GetExecutable("samtools"), false), _tabixWrapper, true);
        }

        public bool RequireNormalVcf()
        {
            return !CustomDbSnpVcf() && RunCnvDetection();
        }

        public bool RunCnvDetection()
        {
            return RunCnvDetection(_detectCnvDefault);
        }

        private bool RunCnvDetection(bool detectCnvDefault)
        {
            return _sampleSettings.GetSetting(GetRunCnvDetectionSetting(detectCnvDefault));
        }

        public Setting<bool> RunCnvDetectionSetting => GetRunCnvDetectionSetting(_detectCnvDefault);


        public static Setting<bool> GetRunCnvDetectionSetting(bool detectCnvDefault)
        {
            return SampleSettings.CreateSetting(
                "RunCNVDetection",
                "Enable/disable CNV Detection step",
                detectCnvDefault,
                null,
                DetectCnvs);
        }

        private static bool DetectCnvs(string name)
        {
            string tempSetting = name.ToLowerInvariant();
            switch (tempSetting)
            {
                case "0":
                case "false":
                case "none":
                    return false;
                case "1":
                case "true":
                case "canvas":
                    return true;
                default:
                    throw new Exception($"Invalid RunCNVDetection setting: {name}");
            }
        }

        private ICanvasWorker<TCanvasInput, TCanvasOutput> GetCanvasWorker<TCanvasInput, TCanvasOutput>(ICanvasCnvCaller<TCanvasInput, TCanvasOutput> canvasCnvCaller, Func<IFileLocation, bool, TCanvasOutput> getFromStub) where TCanvasInput : ICanvasCheckpointInput where TCanvasOutput : ICanvasOutput
        {
            if (!RunCnvDetection(_detectCnvDefault)) return new NullCanvasWorker<TCanvasInput, TCanvasOutput>();

            ICanvasAnnotationFileProvider annotationFileProvider = GetAnnotationFileProvider();
            bool includeIntermediateResults = IncludeIntermediateResults();
            var canvasOutputNamingConventionFactory = new CanvasOutputNamingConventionFactory<TCanvasInput, TCanvasOutput>(annotationFileProvider, includeIntermediateResults, getFromStub);
            var canvasCheckpoint = new CanvasCheckpoint<TCanvasInput, TCanvasOutput>(canvasCnvCaller, canvasOutputNamingConventionFactory);
            return new CanvasWorker<TCanvasInput, TCanvasOutput>(canvasCheckpoint);
        }

        internal bool IncludeIntermediateResults()
        {
            return _sampleSettings.GetSetting(RetainIntermediateCnvFilesSetting);
        }

        public static Setting<bool> RetainIntermediateCnvFilesSetting => SampleSettings
            .CreateSetting(
                "RetainIntermediateCNVFiles",
                "Include intermediate CNV files in the workflow output.",
                false);

        internal IFileLocation GetCanvasExe()
        {
            return new FileLocation(_executableProcessor.GetExecutable("Canvas", "Canvas"));
        }

        internal ICanvasAnnotationFileProvider GetAnnotationFileProvider()
        {
            return new CanvasAnnotationFileProvider(GetDbSnpVcfPath(), new ReferenceGenomeFactory());
        }

        private IFileLocation GetDbSnpVcfPath()
        {
            return _dbSnpVcfProcessor.GetDbSnpVcfPath();
        }

        internal CanvasSingleSampleInputCommandLineBuilder GetCanvasSingleSampleInputCommandLineBuilder(ICanvasAnnotationFileProvider annotationFileProvider)
        {
            var allCustomParams = CommonCustomParams();
            return new CanvasSingleSampleInputCommandLineBuilder(annotationFileProvider, allCustomParams, GetCustomCanvasParameters());
        }

        private string GetCustomCanvasParameters()
        {
            return _executableProcessor.GetExecutableParameters("Canvas");
        }

        private Dictionary<string, string> CommonCustomParams()
        {
            var canvasWorkflowExecutables = new[]
            {
                "CanvasBin", "CanvasClean", "CanvasDiploidCaller", "CanvasNormalize", "CanvasPartition", "CanvasSNV", "CanvasSomaticCaller"
            };
            Dictionary<string, string> allCustomParams = new Dictionary<string, string>();
            foreach (string executable in canvasWorkflowExecutables)
            {
                string customParams = _executableProcessor.GetExecutableParameters(executable);
                if (!Illumina.Common.StringExtensions.IsNullOrWhiteSpace(customParams))
                    allCustomParams.Add(executable, customParams);
            }
            UpdateWithCanvasCountsPerBin(allCustomParams);
            UpdateWithCoverageMode(allCustomParams);
            return allCustomParams;
        }

        private CanvasSingleSampleInputCommandLineBuilder GetCanvasSingleSampleInputCommandLineBuilderWithSomaticQualityThreshold(
            ICanvasAnnotationFileProvider annotationFileProvider)
        {
            var allCustomParams = CommonCustomParams();
            UpdateWithSomaticQualityThreshold(allCustomParams);
            return new CanvasSingleSampleInputCommandLineBuilder(annotationFileProvider, allCustomParams, GetCustomCanvasParameters());
        }

        private void UpdateWithSomaticQualityThreshold(Dictionary<string, string> allCustomParams)
        {
            int? qualityScoreThreshold = _sampleSettings.GetSetting(QualityScoreThresholdSetting);
            if (qualityScoreThreshold.HasValue)
            {
                UpdateCustomParametersWithSetting(allCustomParams, "CanvasSomaticCaller", $" --qualitythreshold {qualityScoreThreshold.Value}");
            }
        }

        public static Setting<int?> QualityScoreThresholdSetting
        {
            get
            {
                return SampleSettings
                    .CreateSetting<int?>(
                        "CanvasQualityScoreThreshold",
                        "Quality score threshold for PASSing variant call",
                        null,
                        nullableInt => nullableInt.HasValue && nullableInt.Value >= 1,
                        value => int.Parse(value));
            }
        }

        private void UpdateWithCanvasCountsPerBin(Dictionary<string, string> allCustomParams)
        {
            int? canvasCountsPerBin = _sampleSettings.GetSetting(CountsPerBinSetting);
            if (canvasCountsPerBin.HasValue)
            {
                UpdateCustomParametersWithSetting(allCustomParams, "CanvasBin", $" -d {canvasCountsPerBin.Value}");
            }
        }

        public static Setting<int?> CountsPerBinSetting
        {
            get
            {
                return SampleSettings
                    .CreateSetting<int?>(
                        "CanvasCountsPerBin",
                        "Median number of read counts per bin",
                        null,
                        nullableInt => nullableInt.HasValue && nullableInt.Value >= 1,
                        value => int.Parse(value));
            }
        }

        private void UpdateWithCoverageMode(Dictionary<string, string> allCustomParams)
        {
            string canvasCoverageMode = _sampleSettings.GetSetting(CanvasCoverageModeSetting);
            if (canvasCoverageMode != null)
            {
                UpdateCustomParametersWithSetting(allCustomParams, "CanvasBin", $" -m {canvasCoverageMode}");
            }
        }

        private void UpdateCustomParametersWithSetting(Dictionary<string, string> allCustomParams, string module, string commandLineParameters)
        {
            string customParams = "";
            string tempParams;
            if (allCustomParams.TryGetValue(module, out tempParams))
            {
                customParams = tempParams;
            }
            customParams += " " + commandLineParameters;
            allCustomParams[module] = customParams;
        }
    }
}