using frou01.GrabController;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

public class GAC_BuildProcess : IProcessSceneWithReport
{
    public int callbackOrder => 0;
    public void OnProcessScene(Scene scene, BuildReport report)
    {
        foreach (GameObject obj in scene.GetRootGameObjects())
        {
            foreach (Controller_Base gacController in obj.GetComponentsInChildren<Controller_Base>(true))
            {
                UdonBehaviour[] udonBehaviours = gacController.GetComponents<UdonBehaviour>();
                bool hasSyncVar = false;
                foreach (UdonBehaviour udon in udonBehaviours)
                {
                    if (udon.SyncMethod != VRC.SDKBase.Networking.SyncType.None)
                    {
                        var type = udon.GetType();
                        FieldInfo memberinfo = type.GetField("serializedProgramAsset",
                            BindingFlags.NonPublic | BindingFlags.Instance);

                        IUdonProgram _program = ((AbstractSerializedUdonProgramAsset)memberinfo.GetValue(udon)).RetrieveProgram();
                        if (_program.SyncMetadataTable != null)
                        {
                            IEnumerable<IUdonSyncMetadata> SyncMetadatas = _program.SyncMetadataTable.GetAllSyncMetadata();
                            foreach (IUdonSyncMetadata metas in SyncMetadatas)
                            {
                                hasSyncVar |= true;
                            }
                        }
                        else { Debug.Log("fail get SyncMetadataTable"); }
                    }
                }
                gacController.NoneSyncMode = !hasSyncVar;
            }
        }
    }
}
