// Ported from the EmpireCraft mod: Scripts/GeneralSystems/InstitutionSystem.cs#InstitutionNodeStatus,InstitutionNodeView @ 55d6d4f8. Body kept line-for-line for three-way sync;
// see Port/manifest.json and Tools/PortTool.
using System;
using System.Collections.Generic;

namespace EmpireCraft.Core
{

public enum InstitutionNodeStatus
{
    Locked,             //本线节点，前置未满足 / 被互斥 / 正统不足
    Available,          //本线节点，可以正常推动
    Forceable,          //本线节点，反对压过支持，只能强制推行
    Reforming,          //本线节点，正在改革中
    Enacted,            //本文化已掌握（自研）
    Absorbed,           //本文化已掌握（从别的线吸收而来）
    ForeignLocked,      //外来节点，等级不够、还吸收不了
    ForeignContacting,  //外来节点，正在接触积累中
    ForeignReady,       //外来节点，接触度已达标，下一次年度结算就会被吸收
    Superseded          //本线节点，已被更新的制度取代(如两税法取代均田租庸)，不能再推行
}

public sealed class InstitutionNodeView
{
    public InstitutionNodeConfig Node;
    public InstitutionNodeStatus Status;
    public float Support;
    public float Opposition;
    public float Exposure;
    public int ContactYears;
    public float RequiredExposure;
    public int RequiredContactYears;
    public string Reason = "";
    // 看的是哪个文化的树(节点名按文化另有叫法，见 InstitutionSystem.GetNodeName)
    public string Culture = "";
}
}
