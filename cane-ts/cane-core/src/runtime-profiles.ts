import type { RuntimeAnimationV1, RuntimeBoneV1, RuntimeConstraintV1 } from "./contracts.js";
import { RuntimeErrorV1 } from "./errors.js";

export const RUNTIME_PROFILES = Object.freeze({
  "constraint.physics.spring-v2": 4,
  "constraint.path.geometry-v2": 5,
  "constraint.ik.affine-v2": 6,
  "constraint.transform.channels-v2": 7,
  "bone.inheritance.polar-v2": 8,
  "animation.curve.roots-v2": 9,
  "animation.track.pose-v2": 10,
  "animation.sequence.clock-v2": 11,
  "mesh.placement": 13,
} as const);
const constraintProfiles: Readonly<Record<string,string>> = {
  physics:"constraint.physics.spring-v2",path:"constraint.path.geometry-v2",ik:"constraint.ik.affine-v2",transform:"constraint.transform.channels-v2",
};
export function constraintProfile(kind:string):string { return constraintProfiles[kind] ?? `constraint.${kind}`; }
const families = {
  boneTimelines:["translate","translateX","translateY","rotate","scale","scaleX","scaleY","shear","shearX","shearY"],
  slotTimelines:["color","alpha"],attachmentTimelines:["region","deform"],constraintTimelines:["keys"],
} as const;
function cubic(value:unknown):boolean {
  if(value===null || typeof value!=="object")return false;
  const record=value as Record<string,unknown>;
  if(record.type==="bezier" || record.type==="bezier-value")return true;
  return record.type==="properties" && (cubic(record.default) || Object.values(record.properties as object).some(cubic));
}
const special=(mode:string):boolean=>mode==="ignoreRotationAndReflection" || mode==="ignoreScale" || mode==="ignoreScaleAndReflection";
export function requiredProfiles(bones:readonly RuntimeBoneV1[],animations:readonly RuntimeAnimationV1[],constraints:readonly RuntimeConstraintV1[]):string[] {
  const result=new Set<string>();
  for(const c of constraints)if(c.type!=="slider")result.add(constraintProfile(c.type));
  if(bones.some(b=>special(b.transformMode)))result.add("bone.inheritance.polar-v2");
  for(const a of animations){
    let pose=a.skins.length>0 || a.drawOrder.length>0 || a.drawOrderFolders.some(f=>f.keys.length>0);
    for(const family of Object.keys(families) as (keyof typeof families)[]){
      for(const timeline of a[family]){
        const t=timeline as unknown as Record<string,unknown>;
        for(const field of families[family]){
          const keys=t[field] as readonly {readonly curve:unknown}[]|null;
          if(keys!==null && keys!==undefined){pose ||= keys.length>0;if(keys.some(k=>cubic(k.curve)))result.add("animation.curve.roots-v2");}
        }
      }
    }
    for(const t of a.boneTimelines){
      pose ||= (t.inherit?.length ?? 0)>0;
      if(t.inherit?.some(k=>special(k.inherit)))result.add("bone.inheritance.polar-v2");
    }
    for(const t of a.slotTimelines)pose ||= (t.attachment?.length ?? 0)>0;
    for(const t of a.attachmentTimelines){
      pose ||= (t.sequence?.length ?? 0)>0;
      if(t.sequence?.some(k=>k.mode!=="fixedFrame"))result.add("animation.sequence.clock-v2");
    }
    if(pose)result.add("animation.track.pose-v2");
  }
  return Array.from(result).sort();
}
export function assertRuntimeProfiles(minor:number,required:readonly string[],profiles:readonly string[]):void {
  if(minor!==0 && (minor<4 || minor>13))throw new RuntimeErrorV1("unsupportedVersion","loadJson","Unsupported Runtime API document version.",{field:"runtimeApiVersion"});
  for(const name of profiles){
    const minimum=RUNTIME_PROFILES[name as keyof typeof RUNTIME_PROFILES];
    if(minor<minimum)throw new RuntimeErrorV1("unsupportedFeature","load",`Playback requires ${name} and Runtime API 1.${minimum}.`,{field:"runtimeApiVersion"});
  }
  for(const name of required){
    const minimum=RUNTIME_PROFILES[name as keyof typeof RUNTIME_PROFILES];
    if(minimum!==undefined && minor<minimum)throw new RuntimeErrorV1("validationFailed","loadJson",`Required profile ${name} is newer than the declared API.`,{field:"runtimeApiVersion"});
  }
}
