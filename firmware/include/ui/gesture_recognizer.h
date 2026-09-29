#pragma once
#include <cstdlib>
namespace streamdeck {
class GestureRecognizer {
 public:
  void begin(int x,int y) { active_=true; emitted_=false; startX_=lastX_=x; startY_=y; }
  int update(int x,int y,bool down) {
    if(!active_) { if(down) begin(x,y); return 0; }
    lastX_=x;
    if(!down) { active_=false; return 0; }
    if(emitted_) return 0;
    const int dx=x-startX_,dy=y-startY_;
    if(std::abs(dx)>=55 && std::abs(dx)>std::abs(dy)*1.35) { emitted_=true; return dx<0?1:-1; }
    return 0;
  }
  bool active() const { return active_; }
 private:
  bool active_=false,emitted_=false;
  int startX_=0,startY_=0,lastX_=0;
};
}
