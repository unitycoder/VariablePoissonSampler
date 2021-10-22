﻿using System;
using System.Collections.Generic;

namespace VertexFragment
{
    /// <summary>
    /// A poisson sampler that operates on any dimension.
    /// Unlike the other implementations, this one is intended to use only standard C# libraries for ease of portability.
    /// </summary>
    public sealed class UniformPoissonSamplerND
    {
        // ---------------------------------------------------------------------------------
        // General Properties
        // ---------------------------------------------------------------------------------

        /// <summary>
        /// Is the poisson sampler currently in progress? If so, any calls to <see cref="Generate"/> will fail.
        /// </summary>
        public bool IsGenerating { get; private set; }

        /// <summary>
        /// The sample point generated by the sampler.
        /// </summary>
        public List<float[]> SamplesList { get; private set; }

        /// <summary>
        /// 
        /// </summary>
        public float[] GridDimensions { get; private set; }

        /// <summary>
        /// The radius of the sample points, aka the minimum distance between points. 
        /// No two sampled points can be any closer than this value, but may be further away.
        /// </summary>
        public float Radius { get; private set; }

        /// <summary>
        /// The maximum number of attempts to generate a valid new point.
        /// The higher this value, the higher the coverage of the sampler but an increased runtime cost.
        /// </summary>
        public int RejectionLimit { get; private set; }

        /// <summary>
        /// The RNG used to generate the points in the sampled annulus.
        /// </summary>
        private Random Rng;

        /// <summary>
        /// 
        /// </summary>
        private int SpatialDimensions;

        // ---------------------------------------------------------------------------------
        // Spatial Grid Properties
        // ---------------------------------------------------------------------------------

        // Note that we implement our own Spatial Grid here and do not use SpatialGrid2D.
        // This is because we know there is a fixed radius and exactly one point per cell and so we are able
        // to take some short cuts for better performance that is not available in the more generic SpatialGrid2D.

        /// <summary>
        /// The length of each side of a cell in the underlying spatial grid.
        /// </summary>
        private float CellLength;

        /// <summary>
        /// 
        /// </summary>
        private int[] CellsPerDimension;

        /// <summary>
        /// The spatial grid used at as a lookup for faster "are there any nearby points?" queries.
        /// Stored as a 1D array, and accessed via <see cref="GetSpatialGridIndex(ref Vector2)"/>.
        /// </summary>
        private List<int> SpatialLookUp;

        /// <summary>
        /// List of candidate points that we will try to generate new points around.
        /// </summary>
        private List<int> ActiveList;

        // ---------------------------------------------------------------------------------
        // Methods
        // ---------------------------------------------------------------------------------

        public UniformPoissonSamplerND(Random rng, float sampleRadius, float[] dimensions, int rejectionLimit = 30)
        {
            Rng = rng;
            Radius = sampleRadius;
            GridDimensions = dimensions;
            RejectionLimit = rejectionLimit;
            SpatialDimensions = dimensions.Length;
        }

        public bool Generate()
        {
            if (IsGenerating)
            {
                return false;
            }

            Initialize();
            GenerateFirstPoint();

            while (ActiveList.Count > 0)
            {
                bool sampleFound = false;
                int activeIndex = GetRandomActiveListIndex();

                float[] sample = SamplesList[ActiveList[activeIndex]];

                for (int i = 0; i < RejectionLimit; ++i)
                {
                    float[] randomSample = GenerateRandomPointInAnnulus(sample);

                    if (!IsSampleOutOfBounds(randomSample) && !IsSampleNearOthers(randomSample))
                    {
                        AddSample(randomSample);
                        sampleFound = true;

                        break;
                    }
                }

                if (!sampleFound)
                {
                    ActiveList[activeIndex] = ActiveList[ActiveList.Count - 1];
                    ActiveList.RemoveAt(ActiveList.Count - 1);
                }
            }


            IsGenerating = false;
            return true;
        }

        /// <summary>
        /// Initializes the sampler for a new run.
        /// </summary>
        private void Initialize()
        {
            IsGenerating = true;
            CellLength = Radius / (float)Math.Sqrt(SpatialDimensions);
            CellsPerDimension = new int[SpatialDimensions];

            int totalCells = 1;

            for (int i = 0; i < SpatialDimensions; ++i)
            {
                CellsPerDimension[i] = (int)Math.Ceiling(GridDimensions[i] / CellLength);
                totalCells *= CellsPerDimension[i];
            }

            SpatialLookUp = new List<int>(totalCells);
            ActiveList = new List<int>(totalCells);
            SamplesList = new List<float[]>();
            
            for (int i = 0; i < totalCells; ++i)
            {
                SpatialLookUp.Add(-1);
            }
        }

        /// <summary>
        /// Generates the first random point in the sample domain and adds it to our collections.
        /// </summary>
        private void GenerateFirstPoint()
        {
            float[] point = new float[SpatialDimensions];

            for (int i = 0; i < SpatialDimensions; ++i)
            {
                point[i] = (float)Rng.NextDouble() * GridDimensions[i];
            }

            AddSample(point);
        }

        /// <summary>
        /// Adds the new sample to the samples list, active list, and spatial grid.
        /// </summary>
        /// <param name="sample"></param>
        private void AddSample(float[] sample)
        {
            int sampleIndex = SamplesList.Count;
            int spatialIndex = GetSpatialGridIndex(sample);

            SamplesList.Add(sample);
            ActiveList.Add(sampleIndex);
            SpatialLookUp[spatialIndex] = sampleIndex;
        }

        /// <summary>
        /// Calculates the index into the spatial grid for the given point.
        /// Does not perform bounds checking.
        /// </summary>
        /// <param name="point"></param>
        /// <returns></returns>
        private int GetSpatialGridIndex(float[] point)
        {
            int[] cells = new int[SpatialDimensions];

            for (int i = 0; i < SpatialDimensions; ++i)
            {
                if ((point[i] < 0.0f) || (point[i] > GridDimensions[i]))
                {
                    return -1;
                }

                cells[i] = (int)(point[i] / CellLength);
            }

            return GetSpatialGridIndex(cells);
        }

        /// <summary>
        /// Calculates the 1D array index for the ND cell indices.
        /// </summary>
        /// <param name="cells"></param>
        /// <returns></returns>
        private int GetSpatialGridIndex(int[] cells)
        {
            int index = 0;

            if (cells[SpatialDimensions - 1] >= 0)
            {
                index = cells[SpatialDimensions - 1];

                if (index >= CellsPerDimension[SpatialDimensions - 1])
                {
                    index = CellsPerDimension[SpatialDimensions - 1] - 1;
                }
            }

            for (int i = (SpatialDimensions - 1); i > 0; --i)
            {
                index *= CellsPerDimension[i - 1];

                if (cells[i - 1] >= 0)
                {
                    int j = cells[i - 1];

                    if (j >= CellsPerDimension[i - 1])
                    {
                        j = CellsPerDimension[i - 1] - 1;
                    }

                    index += j;
                }
            }

            return index;
        }

        /// <summary>
        /// Retrieves a random index from the active list.
        /// </summary>
        /// <returns></returns>
        private int GetRandomActiveListIndex()
        {
            return Rng.Next(ActiveList.Count);
        }

        /// <summary>
        /// Generate a new random point in the annulus around the provided point.
        /// </summary>
        /// <param name="point"></param>
        /// <returns></returns>
        private float[] GenerateRandomPointInAnnulus(float[] point)
        {
            float[] randomVector = GetRandomUnitVectorOnHypersphere(Rng, SpatialDimensions);
            float randomDistance = Lerp(Radius, Radius * 2.0f, (float)Rng.NextDouble());
            float[] newSamplePoint = new float[SpatialDimensions];

            for (int i = 0; i < SpatialDimensions; ++i)
            {
                newSamplePoint[i] = point[i] + randomVector[i] * randomDistance;
            }

            return newSamplePoint;
        }

        /// <summary>
        /// Is the point within bounds of the sample domain?
        /// </summary>
        /// <param name="sample"></param>
        /// <returns></returns>
        private bool IsSampleOutOfBounds(float[] sample)
        {
            for (int i = 0; i < SpatialDimensions; ++i)
            {
                if ((sample[i] < 0.0f) || (sample[i] > GridDimensions[i]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Checks if the sample is near any others by checking neighboring cells.
        /// </summary>
        /// <param name="sample"></param>
        /// <returns></returns>
        private bool IsSampleNearOthers(float[] sample)
        {
            int prospectiveCell = GetSpatialGridIndex(sample);

            if ((prospectiveCell == -1) || SpatialLookUp[prospectiveCell] != -1)
            {
                return true;
            }

            // Find the min and max corners of the hypercube we will be searching for intersections in.
            int[] minCell = new int[SpatialDimensions];
            int[] maxCell = new int[SpatialDimensions];

            for (int i = 0; i < SpatialDimensions; ++i)
            {
                float localMin = Clamp(sample[i] - Radius, 0, GridDimensions[i]);
                float localMax = Clamp(sample[i] + Radius, 0, GridDimensions[i]);

                minCell[i] = (int)(localMin / CellLength);
                maxCell[i] = (int)(localMax / CellLength);
            }

            // Traverse each neighbor and check if there are any intersections.
            int[] current = CopyArray(minCell);

            while (true)
            {
                // This loop is so weird, I know. I tried refactoring the one in the original C++ source to something a bit more normal, but well here we are.
                // Essentially we start at one "corner" of our hypercube (minCell) and iterate ourselves along all n-dimensions until we reach the opposite corner (maxCell).
                int localCell = GetSpatialGridIndex(current);

                if (IsSampleNearSampleInCell(localCell, sample))
                {
                    return true;
                }

                for (int i = 0; i <= SpatialDimensions; ++i)
                {
                    current[i]++;

                    if (current[i] <= maxCell[i])
                    {
                        break;
                    }
                    else
                    {
                        if (i == (SpatialDimensions - 1))
                        {
                            return false;
                        }
                        else
                        {
                            current[i] = minCell[i];
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Checks if the provided sample is near any others in the specified cell.
        /// </summary>
        /// <param name="lookupCell"></param>
        /// <param name="sample"></param>
        /// <returns></returns>
        private bool IsSampleNearSampleInCell(int lookupCell, float[] sample)
        {
            if ((lookupCell < 0) || (lookupCell >= SpatialLookUp.Count))
            {
                return false;
            }

            int cellSampleIndex = SpatialLookUp[lookupCell];

            if (cellSampleIndex == -1)
            {
                return false;
            }

            return SquaredDistanceBetween(sample, SamplesList[cellSampleIndex]) <= (Radius * Radius);
        }

        /// <summary>
        /// Returns the squared distance between the two n-dimensional vectors.
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        private float SquaredDistanceBetween(float[] a, float[] b)
        {
            float squaredDistance = 0.0f;

            for (int i = 0; i < SpatialDimensions; ++i)
            {
                float delta = b[i] - a[i];
                squaredDistance += delta * delta;
            }

            return squaredDistance;
        }

        /// <summary>
        /// Clamps the value.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="min"></param>
        /// <param name="max"></param>
        /// <returns></returns>
        private static float Clamp(float x, float min, float max)
        {
            return (x < min ? min : x > max ? max : x);
        }

        /// <summary>
        /// Returns a unit n-dimensional vector.
        /// </summary>
        /// <param name="rng"></param>
        /// <param name="dimensions"></param>
        /// <returns></returns>
        private static float[] GetRandomUnitVectorOnHypersphere(Random rng, int dimensions)
        {
            float[] randomUnitVector = new float[dimensions];

            for (int i = 0; i < dimensions; ++i)
            {
                randomUnitVector[i] = ((float)rng.NextDouble() * 2.0f) - 1.0f;  // Transform from [0, 1] to [-1, 1]
            }

            float vectorMagnitude = 0.0f;

            for (int i = 0; i < dimensions; ++i)
            {
                vectorMagnitude += randomUnitVector[i] * randomUnitVector[i];
            }

            float squaredMagnitude = (float)Math.Sqrt(vectorMagnitude);

            for (int i = 0; i < dimensions; ++i)
            {
                randomUnitVector[i] /= squaredMagnitude;
            }

            return randomUnitVector;
        }

        /// <summary>
        /// Standard linear interpolation.
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <param name="frac"></param>
        /// <returns></returns>
        private static float Lerp(float a, float b, float frac)
        {
            return (a * (1.0f - frac) + (b * frac));
        }

        /// <summary>
        /// Copies the contents of the source array into a new array.
        /// </summary>
        /// <param name="source"></param>
        /// <returns></returns>
        private static T[] CopyArray<T>(T[] source)
        {
            T[] copy = new T[source.Length];

            for (int i = 0; i < source.Length; ++i)
            {
                copy[i] = source[i];
            }

            return copy;
        }

    }
}
