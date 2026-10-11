/** Exact expansion comparison for cycle * period + offset at binary32 clock boundaries. */
function difference(cycle:number,offset:number,period:number,boundary:number):number[] {
  const product=cycle*period,split=134217729;
  const ca=split*cycle,ah=ca-(ca-cycle),al=cycle-ah,cb=split*period,bh=cb-(cb-period),bl=period-bh;
  const residual=((ah*bh-product)+ah*bl+al*bh)+al*bl;
  let parts:number[]=[];
  for(const term of [product,residual,offset,-boundary]){let accumulator=term;const next:number[]=[];
    for(const part of parts){const sum=accumulator+part,recovered=sum-accumulator,error=(accumulator-(sum-recovered))+(part-recovered);if(error!==0)next.push(error);accumulator=sum;}
    if(accumulator!==0 || next.length===0)next.push(accumulator);parts=next;}
  return parts;
}
export interface EventBounds { low:number; high:number; includeLow:boolean; includeHigh:boolean; }
export function eventIntegerBounds(bounds:EventBounds,offset:number,period:number):[number,number]|null {
  const low=(bounds.low-offset)/period,high=(bounds.high-offset)/period;
  let first=bounds.includeLow?Math.ceil(low):Math.floor(low)+1,last=bounds.includeHigh?Math.floor(high):Math.ceil(high)-1;
  if(Math.abs(low)<=4503599627370492 && Math.abs(high)<=4503599627370492){
    const above=(i:number)=>{const v=difference(i,offset,period,bounds.low).at(-1)!;return v>0 || (v===0&&bounds.includeLow);};
    const below=(i:number)=>{const v=difference(i,offset,period,bounds.high).at(-1)!;return v<0 || (v===0&&bounds.includeHigh);};
    while(!above(first))first++;while(above(first-1))first--;while(!below(last))last--;while(below(last+1))last++;
  }
  return first<=last?[first,last]:null;
}
export function eventRelativePosition(cycle:number,offset:number,period:number,origin:number):number {
  return difference(cycle,offset,period,origin).reduce((sum,part)=>sum+part,0);
}
