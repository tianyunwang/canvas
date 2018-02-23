﻿using System.Collections.Generic;
using System.IO;
using System.Linq;
using CanvasCommon;
using Illumina.Common;
using Illumina.Common.FileSystem;
using Isas.Framework.DataTypes;
using Isas.Framework.DataTypes.Maps;

namespace CanvasPedigreeCaller
{
    internal class JointLikelihoods
    {
        public double MaximalLogLikelihood;
        private readonly Dictionary<ISampleMap<Genotype>, double> _jointLikelihoods;
        public double TotalMarginalLikelihood { get; private set; }

        public JointLikelihoods()
        {
            MaximalLogLikelihood = double.NegativeInfinity;
            var comparer = new SampleGenotypeComparer();
            _jointLikelihoods = new Dictionary<ISampleMap<Genotype>, double>(comparer);
            TotalMarginalLikelihood = 0;
        }


        public void AddJointLikelihood(ISampleMap<Genotype> samplesGenotypes, double likelihood)
        {
            if (_jointLikelihoods.ContainsKey(samplesGenotypes) && _jointLikelihoods[samplesGenotypes] < likelihood)
            {
                TotalMarginalLikelihood = TotalMarginalLikelihood + (likelihood - _jointLikelihoods[samplesGenotypes]);
                _jointLikelihoods[samplesGenotypes] = likelihood;

            }
            else if (!_jointLikelihoods.ContainsKey(samplesGenotypes))
            {
                TotalMarginalLikelihood = TotalMarginalLikelihood + likelihood;
                _jointLikelihoods[samplesGenotypes] = likelihood;
            }

        }

        public double GetJointLikelihood(ISampleMap<Genotype> samplesGenotypes)
        {
            return _jointLikelihoods[samplesGenotypes];
        }

        public double GetMarginalGainDeNovoLikelihood(KeyValuePair<SampleId, Genotype> probandRefPloidy, KeyValuePair<SampleId, Genotype> parent1RefPloidy,
            KeyValuePair<SampleId, Genotype> parent2RefPloidy)
        {
            return _jointLikelihoods.Where(
                    // proband is more than ref ploidy
                    kvp => kvp.Key[probandRefPloidy.Key].More(probandRefPloidy.Value) &&
                    // parent1 equals or less than ref ploidy
                    !kvp.Key[parent1RefPloidy.Key].More(parent1RefPloidy.Value) &&
                    // parent2 equals or less than ref ploidy
                    !kvp.Key[parent2RefPloidy.Key].More(parent2RefPloidy.Value))
                .Select(kvp => kvp.Value).Sum();
        }

        public double GetMarginalLossDeNovoLikelihood(KeyValuePair<SampleId, Genotype> probandRefPloidy, KeyValuePair<SampleId, Genotype> parent1RefPloidy,
            KeyValuePair<SampleId, Genotype> parent2RefPloidy)
        {
            return _jointLikelihoods.Where(
                // proband is less than ref ploidy
                kvp => kvp.Key[probandRefPloidy.Key].Less(probandRefPloidy.Value) &&
                // parent1 equals or more than ref ploidy
                !kvp.Key[parent1RefPloidy.Key].Less(parent1RefPloidy.Value) &&
                // parent2 equals or more than ref ploidy
                !kvp.Key[parent2RefPloidy.Key].Less(parent2RefPloidy.Value))
            .Select(kvp => kvp.Value).Sum();
        }

        // in a pedigree with the map (SampleId[M]=>Genotype[G], M: parents, offspring, G: genotype), estimate posterior likelihood as
        // (SampleId[M]=>Genotype[G], sum over all M=m, G=g)/(SampleId[M]=>Genotype[G], sum over all M and G, i.e. what is the probability of 
        // pedigree member X having genotype Y
        public double GetMarginalLikelihood(KeyValuePair<SampleId, Genotype> samplesGenotype)
        {
            return _jointLikelihoods.Where(kvp => Equals(kvp.Key[samplesGenotype.Key], samplesGenotype.Value)).Select(kvp => kvp.Value).Sum() /
                TotalMarginalLikelihood;
        }

        // in a pedigree with the map (SampleId[M]=>Genotype[G], M: parents, offspring, G: genotype), estimate posterior likelihood as
        // (SampleId[M]=>Genotype[G], sum over all M!=m, G!=g)/(SampleId[M]=>Genotype[G], sum over all M and G, i.e. what is the probability of 
        // pedigree member X not having genotype Y
        public double GetMarginalNonAltLikelihood(KeyValuePair<SampleId, Genotype> samplesGenotype)
        {
            return _jointLikelihoods.Where(kvp => !Equals(kvp.Key[samplesGenotype.Key], samplesGenotype.Value)).Select(kvp => kvp.Value).Sum() /
                TotalMarginalLikelihood;
        }

        private class SampleGenotypeComparer : IEqualityComparer<ISampleMap<Genotype>>
        {
            public bool Equals(ISampleMap<Genotype> x, ISampleMap<Genotype> y)
            {
                return x.SequenceEqual(y);
            }

            public int GetHashCode(ISampleMap<Genotype> obj)
            {
                return obj.Aggregate(17, (hash, value) => hash + value.GetHashCode() * 31);
            }
        }
    }
}