import type { AffineV1, BoneTransformModeV1, RuntimeBoneV1, RuntimeBoneLocalV1, RuntimeFrameV1, RuntimeSetupBoneV1 } from "./contracts.js";
import { RuntimeErrorV1 } from "./errors.js";
import { childWorldAffineV1, localBoneAffineV1, IDENTITY_AFFINE_V1 } from "./math/affine.js";
import { recoverLocalTransform, checkWorld } from "./math/transform-channels.js";
const radiansToDegrees = (value:number):number => Math.fround(value * Math.fround(180 / Math.fround(Math.PI)));

const MODES = new Set<BoneTransformModeV1>(["inheritAll", "translationOnly", "ignoreRotationAndReflection", "ignoreScale", "ignoreScaleAndReflection"]);
function checkedLocal(local: RuntimeBoneLocalV1, op: string): void {
  if (![local.x,local.y,local.rotationDegrees,local.scaleX,local.scaleY,local.shearXDegrees,local.shearYDegrees].every(v => Number.isFinite(v) && Number.isFinite(Math.fround(v))))
    throw new RuntimeErrorV1("invalidArgument",op,"Local pose components must be finite binary32 values.",{field:"local"});
}
function bone(local:RuntimeBoneLocalV1,id:string,parentId:string|null,mode:BoneTransformModeV1):RuntimeBoneV1 {
  return { id,name:id,parentId,length:0,x:Math.fround(local.x),y:Math.fround(local.y),rotation:Math.fround(local.rotationDegrees),
    scaleX:Math.fround(local.scaleX),scaleY:Math.fround(local.scaleY),shearX:Math.fround(local.shearXDegrees),shearY:Math.fround(local.shearYDegrees),transformMode:mode };
}
/** Authored hierarchy only; no animation, constraints, skin filtering or host transform. */
export function setupBoneFrameV1(input:readonly RuntimeSetupBoneV1[]):RuntimeFrameV1 {
  const op="querySetupBones", ids=new Map<string,number>(), worlds:AffineV1[]=[], state=new Uint8Array(input.length);
  const values=input.map((value,i)=>{
    checkedLocal(value.local,op);
    if(!value.id.trim() || ids.has(value.id) || !MODES.has(value.transformMode))throw new RuntimeErrorV1("validationFailed",op,"Invalid or duplicate setup bone.",{entityId:value.id});
    ids.set(value.id,i);return bone(value.local,value.id,value.parentId,value.transformMode);
  });
  for(let start=0;start<values.length;start++){
    if(state[start]===2)continue;
    const path:number[]=[];let cursor:number|undefined=start;
    while(cursor!==undefined && state[cursor]!==2){
      const value=values[cursor]!;
      if(state[cursor]===1)throw new RuntimeErrorV1("validationFailed",op,"Setup hierarchy contains a cycle.",{entityId:value.id});
      state[cursor]=1;path.push(cursor);
      if(value.parentId===null)cursor=undefined;
      else {cursor=ids.get(value.parentId);if(cursor===undefined)throw new RuntimeErrorV1("validationFailed",op,"Missing setup parent.",{entityId:value.id});}
    }
    for(let i=path.length-1;i>=0;i--){const index=path[i]!,v=values[index]!,parent=v.parentId===null?undefined:worlds[ids.get(v.parentId)!];
      const world=parent===undefined?localBoneAffineV1(v):childWorldAffineV1(parent,v);checkWorld(world);worlds[index]=world;state[index]=2;}
  }
  return {sequence:1,timeSeconds:0,bones:values.map((v,i)=>{const matrix=worlds[i]!;return {id:v.id,matrix,x:matrix.tx,y:matrix.ty,
    rotationDegrees:radiansToDegrees(Math.fround(Math.atan2(matrix.b,matrix.a))),scaleX:Math.fround(Math.hypot(matrix.a,matrix.b)),scaleY:Math.fround(Math.hypot(matrix.c,matrix.d))};}),
    activeSkinIds:[],sampledSkinIds:[],events:[],renderPacket:{coordinateSystem:"xRightYUp",uvOrigin:"topLeft",tintColorSpace:"srgb",tintAlphaMode:"straight",attachments:[]}};
}
export function solveBoneLocalForWorldV1(parent:AffineV1|null,world:AffineV1,mode:BoneTransformModeV1,reference:RuntimeBoneLocalV1):RuntimeBoneLocalV1 {
  const op="solveBoneLocalForWorld";checkedLocal(reference,op);
  if(!MODES.has(mode) || ![world,...(parent===null?[]:[parent])].every(m=>[m.a,m.b,m.c,m.d,m.tx,m.ty].every(v=>Number.isFinite(v)&&Number.isFinite(Math.fround(v)))))
    throw new RuntimeErrorV1("invalidArgument",op,"Affine components and inheritance mode must be valid.");
  try {const result=recoverLocalTransform(world,parent??IDENTITY_AFFINE_V1,parent===null?"inheritAll":mode,bone(reference,"",null,mode));
    return {x:result.x,y:result.y,rotationDegrees:result.rotation,scaleX:result.scaleX,scaleY:result.scaleY,shearXDegrees:result.shearX,shearYDegrees:result.shearY};
  }catch(cause){throw new RuntimeErrorV1("invalidState",op,"Requested world pose is unreachable under the selected parent and inheritance mode.",{cause});}
}
