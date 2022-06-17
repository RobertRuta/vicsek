using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using BufferSorter;

public class ParticleController : MonoBehaviour
{
    #region variables

    public ComputeShader ParticleCalculation;
    public ComputeShader SortShader;
    public Material ParticleMaterial;
    public int numParticles = 500000;
    public float speed = 4.0f;
    public float radius = 5.0f;
    public Vector3 box = new Vector3(1, 1, 1);

    private float cellDim;
    private Vector3Int gridDims;

    private const int c_groupSize = 128;
    private int m_buildGridIndicesKernel;
    private int m_updateParticlesKernel;
    

    #endregion


    #region Particle Struct

    struct Particle
    {
        public Vector3 position;
        public Vector3 velocity;
        public Vector3 color;
    }    
        
    #endregion


    #region Buffers

    private ComputeBuffer m_particlesBuffer;
    private Particle[] temp_particles;
    private const int c_particleStride = 36;
    private ComputeBuffer m_quadPoints;
    private const int c_quadStride = 12;

    private ComputeBuffer m_indicesBuffer;
    private ComputeBuffer m_valueBuffer;
    private ComputeBuffer m_keyBuffer;
    private Vector2Int[] temp_indices;

    private int numGroups;

    #endregion


    #region setup

    void Start()
    {
        // Find kernel and set kernel indices
        m_updateParticlesKernel = ParticleCalculation.FindKernel("UpdateParticles");
        m_buildGridIndicesKernel = ParticleCalculation.FindKernel("UpdateGrid");

        
        // Create buffers
            // Create particle buffer
        m_particlesBuffer = new ComputeBuffer(numParticles, c_particleStride);
            // Create quad buffer
        m_quadPoints = new ComputeBuffer(6, c_quadStride);
            // Create index buffer
        m_indicesBuffer = new ComputeBuffer(numParticles, 32);
        m_valueBuffer = new ComputeBuffer(numParticles, 16);
        m_keyBuffer = new ComputeBuffer(numParticles, 16);


        // Initialise particles in memory and set initial positions and velocities
        Particle[] particles = new Particle[numParticles];
        InitParticles(particles, m_particlesBuffer);


        // Initialise grid
        cellDim = radius;
        CalcGrid();


        // Initialise quad points
        InitQuads(m_quadPoints);


        // Set radius and cellDim variables in computer shader
        ParticleCalculation.SetFloat("radius", radius);
        ParticleCalculation.SetFloat("cellDim", Mathf.CeilToInt(radius));
        ParticleCalculation.SetFloats("box", new[] {box.x, box.y, box.z});


        // Testing
        // Prepare to run GPU code
        numGroups = Mathf.CeilToInt((float)numParticles / c_groupSize);

        ParticleCalculation.SetBuffer(m_buildGridIndicesKernel, "particleIDs", m_keyBuffer);
        ParticleCalculation.SetBuffer(m_buildGridIndicesKernel, "cellIDs", m_valueBuffer);
        ParticleCalculation.SetBuffer(m_buildGridIndicesKernel, "particles", m_particlesBuffer);
            // Run code
        ParticleCalculation.Dispatch(m_buildGridIndicesKernel, numGroups, 1, 1);
        
        int[] temp_values = new int[numParticles];
        m_valueBuffer.GetData(temp_values);
        int[] temp_keys = new int[numParticles];
        m_keyBuffer.GetData(temp_keys);
        Particle[] temp_particles = new Particle[numParticles];
        m_particlesBuffer.GetData(temp_particles);
        
        for (int i = 0; i < 5; i++)
        {
            print("particle " + i + ": " + temp_keys[i] + " | " + temp_values[temp_keys[i]] + "    pos = " + temp_particles[temp_keys[i]].position);
            //print(temp_values[i]);
        }

        print("");
        print("Sorting somehow...");
        print("");

        /*
        */
        using (Sorter sorter = new Sorter(SortShader))
        {
            // values and keys have to be separate compute buffers
            sorter.Sort(m_valueBuffer, m_keyBuffer);
        }

        m_valueBuffer.GetData(temp_values);
        m_keyBuffer.GetData(temp_keys);
        
        for (int i = 0; i < 5; i++)
        {
            print("particle " + i + ": " + temp_keys[i] + " | " + temp_values[temp_keys[i]] + "    pos = " + temp_particles[temp_keys[i]].position);
            //print(temp_values[i]);
        }

    }

    #endregion


    #region Compute Update

    void Update()
    {
        // Updated compute shader variables
        ParticleCalculation.SetFloat("deltaTime", Time.deltaTime);
        ParticleCalculation.SetFloat("speed", speed);
        
        // Recalculate grid on box rescale
        CalcGrid();
            // Send new box dimensions to GPU
        ParticleCalculation.SetFloats("box", new[] {box.x, box.y, box.z});
        ParticleCalculation.SetInts("gridDims", new[] {gridDims.x, gridDims.y, gridDims.z});


        // Dispatch particle update code on GPU
            // Update GPU buffer with CPU buffer ?? Other way round?
        ParticleCalculation.SetBuffer(m_updateParticlesKernel, "particles", m_particlesBuffer);
            // Run code
        ParticleCalculation.Dispatch(m_updateParticlesKernel, numGroups, 1, 1);

        // Dispatch grid update code on GPU
            // Update GPU buffer with CPU buffer ?? Other way round?
        ParticleCalculation.SetBuffer(m_buildGridIndicesKernel, "particleIDs", m_keyBuffer);
        ParticleCalculation.SetBuffer(m_buildGridIndicesKernel, "cellIDs", m_valueBuffer);
            // Update GPU buffer with CPU buffer ?? Other way round?
        ParticleCalculation.SetBuffer(m_buildGridIndicesKernel, "particles", m_particlesBuffer);
            // Run code
        ParticleCalculation.Dispatch(m_buildGridIndicesKernel, numGroups, 1, 1);

        /*
        */
        using (Sorter sorter = new Sorter(SortShader))
        {
            // values and keys have to be separate compute buffers
            sorter.Sort(m_valueBuffer, m_keyBuffer);
        }

        #region debugging
        
        int[] temp_values = new int[numParticles];
        m_valueBuffer.GetData(temp_values);
        int[] temp_keys = new int[numParticles];
        m_keyBuffer.GetData(temp_keys);

            /*
        for (int i = 0; i < 10; i++)
        {
            print(i + ": " + temp_keys[i] + " | " + temp_values[i]);
            //max = max < temp_indices[i].y ? temp_indices[i].y : max;
            if (max < temp_indices[i].y)
            {
                max = temp_indices[i].y;
            }
        }
            */
        
        #endregion

    }

    #endregion


    #region Rendering
        void OnRenderObject()
        {
            ParticleMaterial.SetBuffer("particles", m_particlesBuffer);
            ParticleMaterial.SetBuffer("quadPoints", m_quadPoints);

            ParticleMaterial.SetPass(0);

            Graphics.DrawProceduralNow(MeshTopology.Triangles, 6, numParticles);
        }
    #endregion


    #region Cleanup
    void OnDestroy()
    {
        m_particlesBuffer.Dispose();
        m_quadPoints.Dispose();
        m_indicesBuffer.Dispose();
    }
    #endregion

    
    #region HelperFunctions

    void InitParticles(Particle[] particles, ComputeBuffer pBuffer)
    {
        for (int i = 0; i < numParticles; i++)
        {
            particles[i].position = new Vector3(Random.Range(0.0f, box.x), Random.Range(0.0f, box.y), Random.Range(0.0f, box.z));
            particles[i].velocity = new Vector3(Random.Range(-1.0f, 1.0f), Random.Range(-1.0f, 1.0f), Random.Range(-1.0f, 1.0f)).normalized * speed;
        }
            // Pack this data into buffer on CPU
        pBuffer.SetData(particles);
    }

    void RecalcBox()
    {
        // Recalculate box dimensions
        box.x = Mathf.Round(box.x / cellDim) * cellDim;
        box.y = Mathf.Round(box.y / cellDim) * cellDim;
        box.z = Mathf.Round(box.z / cellDim) * cellDim;
    }

    void CalcGrid()
    {
        RecalcBox();
        // How many cells along x, y, z axes
        gridDims = new Vector3Int((int) (box.x / cellDim) , (int) (box.y / cellDim), (int) (box.z / cellDim));
    }

    void InitQuads(ComputeBuffer quadBuffer)
    {
        // Initialise quadpoints buffer (individual particles)
        quadBuffer.SetData(new[] {
            new Vector3(-0.25f, 0.25f),
            new Vector3(0.25f, 0.25f),
            new Vector3(0.25f, -0.25f),
            new Vector3(0.25f, -0.25f),
            new Vector3(-0.25f, -0.25f),
            new Vector3(-0.25f, 0.25f),
        });
    }

    #endregion

}
