#pragma once
#include <array>
#include <cmath>
#include <optional>
#include <utility>

namespace cane::animation::event_lattice {
struct Bounds {
    double low, high; bool include_low, include_high;
    bool contains(double value) const { return (value>low || (include_low&&value==low)) && (value<high || (include_high&&value==high)); }
};
struct Expansion { std::array<double,4> parts{}; std::size_t size=0; };
inline Expansion difference(double cycle,double offset,double period,double boundary) {
    const double product=cycle*period,residual=std::fma(cycle,period,-product); Expansion result;
    for(double term : {product,residual,offset,-boundary}) {
        double acc=term;Expansion next;
        for(std::size_t i=0;i<result.size;++i){const auto part=result.parts[i],sum=acc+part,recovered=sum-acc,error=(acc-(sum-recovered))+(part-recovered);if(error!=0)next.parts[next.size++]=error;acc=sum;}
        if(acc!=0 || next.size==0)next.parts[next.size++]=acc;
        result=next;
    }
    return result;
}
inline std::optional<std::pair<double,double>> indices(const Bounds& bounds,double offset,double period) {
    const auto low=(bounds.low-offset)/period,high=(bounds.high-offset)/period;
    auto first=bounds.include_low?std::ceil(low):std::floor(low)+1,last=bounds.include_high?std::floor(high):std::ceil(high)-1;
    if(std::abs(low)<=4503599627370492.0 && std::abs(high)<=4503599627370492.0){
        const auto above=[&](double i){const auto p=difference(i,offset,period,bounds.low);const auto v=p.parts[p.size-1];return v>0 || (v==0&&bounds.include_low);};
        const auto below=[&](double i){const auto p=difference(i,offset,period,bounds.high);const auto v=p.parts[p.size-1];return v<0 || (v==0&&bounds.include_high);};
        while(!above(first))++first;
        while(above(first-1))--first;
        while(!below(last))--last;
        while(below(last+1))++last;
    }
    if(first>last)return {};
    return std::make_pair(first,last);
}
inline double relative(double cycle,double offset,double period,double origin) {const auto p=difference(cycle,offset,period,origin);double sum=0;for(std::size_t i=0;i<p.size;++i)sum+=p.parts[i];return sum;}
}
