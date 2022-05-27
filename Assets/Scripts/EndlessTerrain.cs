using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Threading;

public class EndlessTerrain : MonoBehaviour
{

    const float scale = 5f;

    const float viewerMoveThresholdForChunkUpdate = 25f;
    const float sqrViewerMoveThresholdForChunkUpdate = viewerMoveThresholdForChunkUpdate * viewerMoveThresholdForChunkUpdate;

    public static float maxViewDist;
    public LODInfo[] detailLevels;

    public Transform viewer;
    public Material mapMaterial;

    static List<TerrainChunk> terrainChunksVisibleLastUpdate = new List<TerrainChunk>();

    public static Vector2 viewerPosition;
    Vector2 viewerPositionOld;
    static MapGenerator mapGenerator;
    int chunkSize;
    int chunksVisibleInViewDist;

    Dictionary<Vector2, TerrainChunk> terrainChunkDictionary = new Dictionary<Vector2, TerrainChunk>();

    void Start()
    {
        mapGenerator = FindObjectOfType<MapGenerator>();

        maxViewDist = detailLevels[detailLevels.Length - 1].visibleDstThreshold;
        chunkSize = MapGenerator.mapChunkSize - 1;
        chunksVisibleInViewDist = Mathf.RoundToInt(maxViewDist / chunkSize);

        UpdateVisibleChunks();
    }

    void Update()
    { //this is run when generating terrain while playing the game
        viewerPosition = new Vector2(viewer.position.x, viewer.position.z) / scale;

        if((viewerPositionOld - viewerPosition).sqrMagnitude > sqrViewerMoveThresholdForChunkUpdate)
        {
            viewerPositionOld = viewerPosition;
            UpdateVisibleChunks();
        }
    }

    void UpdateVisibleChunks()
    { 
        //this function is called on each frame to check all the chunks in visible range for the player transform  
        for (int i = 0; i < terrainChunksVisibleLastUpdate.Count; i++)
        {
            terrainChunksVisibleLastUpdate[i].SetVisible(false);
        }
        terrainChunksVisibleLastUpdate.Clear();

        int currentChunkCoordX = Mathf.RoundToInt(viewerPosition.x / chunkSize);
        int currentChunkCoordY = Mathf.RoundToInt(viewerPosition.y / chunkSize);

        for (int yOffset = -chunksVisibleInViewDist; yOffset <= chunksVisibleInViewDist; yOffset++)
        {
            //these two loops scan all the chunks that will be visible to the transform from bottom left to top right
            for (int xOffset = -chunksVisibleInViewDist; xOffset <= chunksVisibleInViewDist; xOffset++)
            {
                Vector2 viewedChunkCoord = new Vector2(currentChunkCoordX + xOffset, currentChunkCoordY + yOffset); 
                //coordinates of the current chunk that we are updating

                if (terrainChunkDictionary.ContainsKey(viewedChunkCoord))
                {
                    //if the chunk that is visible has already been generated once then it is updates and set to visible.
                    terrainChunkDictionary[viewedChunkCoord].UpdateTerrainChunk();
                    // if (terrainChunkDictionary[viewedChunkCoord].isVisible()) //if after the current update this chunk was visible
                    // {
                    //     terrainChunksVisibleLastUpdate.Add(terrainChunkDictionary[viewedChunkCoord]);
                    //     //the chunk is then added to the lastvisible chunks list so that it can be reset in the next frame and is checked again for visibility
                    // }
                }
                else
                {
                    //if the chunk is not there in the dictionary, new chunk is generated and added to the dictionary
                    terrainChunkDictionary.Add(viewedChunkCoord, new TerrainChunk(viewedChunkCoord, chunkSize, detailLevels, transform, mapMaterial));
                }
            }
        }
    }

    public class TerrainChunk
    {
        GameObject meshObject;
        Vector2 position;
        Bounds bounds;
        //this is the bounding box of the chunk

        MeshRenderer meshRenderer;
        MeshFilter meshFilter;

        LODInfo[] detailLevels;
        LODMesh[] lodMeshes;

        MapData mapData;
        bool mapDataRecieved;
        int previousLODIndex = -1;

        public TerrainChunk(Vector2 coord, int size, LODInfo[] detailLevels, Transform parent, Material material)
        {
            this.detailLevels = detailLevels;
            //generation of a new chunk is done here by calling a thread for it
            
            position = coord * size;
            bounds = new Bounds(position, Vector2.one * size);
            //bounding box of the chunk is exactly of the size of the chunk
            Vector3 positionV3 = new Vector3(position.x, 0, position.y);

            meshObject = new GameObject("Terrain Chunk");
            meshRenderer = meshObject.AddComponent<MeshRenderer>();
            meshFilter = meshObject.AddComponent<MeshFilter>();
            meshRenderer.material = material;

            meshObject.transform.position = positionV3 * scale;
            //meshObject.transform.localScale = Vector3.one * size / 10f;
            meshObject.transform.parent = parent;
            meshObject.transform.localScale = Vector3.one * scale;

            SetVisible(false);

            lodMeshes = new LODMesh[detailLevels.Length];
            for(int i = 0; i < detailLevels.Length; i++)
            {
                lodMeshes[i] = new LODMesh(detailLevels[i].lod, UpdateTerrainChunk);
            }

            //all right threading part here 
            //RequestMapData is called from the mapgenerator class by passing a onMapDataRecieved function as parameter
            //in RequestMapData, a thread is first created by creating a lambda function that calls the MapDataThread function and passes the same onMapDataRecieved function as parameter there
            //This thread with the lambda function is then started, first then calling the MapDataThread function with the parameter onMapDataRecieved function
            //Map Data is generated for the chunk(for now, all chunks have the same seed and no offset so all are the same data) and the Queue that stores MapThreadInfo<MapData>
            //MapThreadInfo<T> is a struct that simply stores an Action<T> callback and a T parameter with its constructor.
            //The thread after generating the chunk first locks the queue meshDataThreadInfoQueue and enqueues a new MapThreadInfo<MapData>(callback, mapData).
            //mapData here is the recently generated mapData and callback will be the initially passed onMapDataRecieved function(EndlessTerrain.cs).
            
            //Now in update()(MapGenerator.cs) the mapThreadInfoQueue is dequeued one by one by taking out MapThreadInfo<MapData> from the queue and the callback from the struct is called: 
            //threadInfo.callback(threadInfo.parameter) where callback is the function onMapDataRecieved function(EndlessTerrain.cs) and parameter is the mapData generated in the thread.

            //In onMapDataRecieved function, mapGenerator.RequestMeshData(mapData, OnMeshDataRecieved) is invoked.
            //Now in RequestMeshData function(MapGenerator.cs), again, a thread is created by the function in a similar way as before with a MeshDataThread and meshDataThreadInfoQueue.
            //And similarly as before, the queue is enqueued and dequeued and after the MeshData queue is dequeued, we get the meshData in the EndlessTerrain.cs script and the mesh is applied to the mesh.
            mapGenerator.RequestMapData(position, onMapDataRecieved);
        }

        void onMapDataRecieved(MapData mapData)
        {
            //mapGenerator.RequestMeshData(mapData, OnMeshDataRecieved);
            this.mapData = mapData;
            mapDataRecieved = true;

            Texture2D texture = TextureGenerator.TextureFromColorMap(mapData.colourMap, MapGenerator.mapChunkSize, MapGenerator.mapChunkSize);
            meshRenderer.material.mainTexture = texture;

            UpdateTerrainChunk();
        }

        // void OnMeshDataRecieved(MeshData meshData)
        // {
        //     meshFilter.mesh = meshData.CreateMesh();
        // }

        public void UpdateTerrainChunk()
        {
            if(mapDataRecieved) 
            {
                float viewerDistFromNearestEdge = Mathf.Sqrt(bounds.SqrDistance(viewerPosition));
                //calculates the distance of the neearest point of the chunk to the viewer and if it is within view distance, is set to visible 
                bool visible = viewerDistFromNearestEdge <= maxViewDist;

                if(visible)
                {
                    int lodIndex = 0;
                    for(int i = 0; i < detailLevels.Length - 1; i++) 
                    {
                        if(viewerDistFromNearestEdge > detailLevels[i].visibleDstThreshold)
                        {
                            lodIndex = i + 1;
                        }
                        else 
                        {
                            break;
                        }
                    }

                    if(lodIndex != previousLODIndex)
                    {
                        LODMesh lodMesh = lodMeshes[lodIndex];
                        if(lodMesh.hasMesh)
                        {
                            previousLODIndex = lodIndex;
                            meshFilter.mesh = lodMesh.mesh;
                        }
                        else if(!lodMesh.hasRequestedMesh)
                        {
                            lodMesh.RequestMesh(mapData);
                        }
                    }

                    terrainChunksVisibleLastUpdate.Add(this);
                }

                SetVisible(visible);
            }
        }

        public void SetVisible(bool visible)
        {
            meshObject.SetActive(visible);
        }

        public bool isVisible()
        {
            return meshObject.activeSelf;
        }
    }

    class LODMesh 
    {
        public Mesh mesh;
        public bool hasRequestedMesh;
        public bool hasMesh;
        int lod;
        System.Action updateCallback;

        public LODMesh(int lod, System.Action updateCallback)
        {
            this.lod = lod;
            this.updateCallback = updateCallback;
        }

        void OnMeshDataRecieved(MeshData meshData)
        {
            mesh = meshData.CreateMesh();
            hasMesh = true;

            updateCallback();
        }

        public void RequestMesh(MapData mapData)
        {
            hasRequestedMesh = true;
            mapGenerator.RequestMeshData(mapData, lod, OnMeshDataRecieved);
        }
    }

    [System.Serializable]
    public struct LODInfo 
    {
        public int lod;
        public float visibleDstThreshold;
    }
}
