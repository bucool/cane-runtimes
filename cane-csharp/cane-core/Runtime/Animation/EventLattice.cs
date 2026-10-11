using System;
using System.Collections.Generic;
namespace Cane.Animation
{
    internal readonly struct EventBounds
    {
        internal readonly double Low, High;
        internal readonly bool IncludeLow, IncludeHigh;
        internal EventBounds(double low, bool includeLow, double high, bool includeHigh)
        { Low=low; High=high; IncludeLow=includeLow; IncludeHigh=includeHigh; }
        internal bool Contains(double value) => (value>Low || (IncludeLow&&value==Low)) && (value<High || (IncludeHigh&&value==High));
    }
    internal static class EventLattice
    {
        private static List<double> Difference(double cycle,double offset,double period,double boundary)
        {
            double product=cycle*period,ca=134217729*cycle,ah=ca-(ca-cycle),al=cycle-ah,cb=134217729*period,bh=cb-(cb-period),bl=period-bh;
            double residual=((ah*bh-product)+ah*bl+al*bh)+al*bl;var parts=new List<double>();
            foreach(double term in new[]{product,residual,offset,-boundary}){double acc=term;var next=new List<double>();
                foreach(double part in parts){double sum=acc+part,recovered=sum-acc,error=(acc-(sum-recovered))+(part-recovered);if(error!=0)next.Add(error);acc=sum;}
                if(acc!=0 || next.Count==0)next.Add(acc);parts=next;}
            return parts;
        }
        private static double Sign(double cycle,double offset,double period,double boundary){var p=Difference(cycle,offset,period,boundary);return p[p.Count-1];}
        internal static (double First,double Last)? Indices(EventBounds bounds,double offset,double period)
        {
            double low=(bounds.Low-offset)/period,high=(bounds.High-offset)/period;
            double first=bounds.IncludeLow?Math.Ceiling(low):Math.Floor(low)+1,last=bounds.IncludeHigh?Math.Floor(high):Math.Ceiling(high)-1;
            if(Math.Abs(low)<=4503599627370492 && Math.Abs(high)<=4503599627370492){
                bool Above(double i){double v=Sign(i,offset,period,bounds.Low);return v>0 || (v==0&&bounds.IncludeLow);}
                bool Below(double i){double v=Sign(i,offset,period,bounds.High);return v<0 || (v==0&&bounds.IncludeHigh);}
                while(!Above(first))first++;while(Above(first-1))first--;while(!Below(last))last--;while(Below(last+1))last++;
            }
            return first<=last?(first,last):((double,double)?)null;
        }
        internal static double Relative(double cycle,double offset,double period,double origin){double sum=0;foreach(double part in Difference(cycle,offset,period,origin))sum+=part;return sum;}
    }
}
