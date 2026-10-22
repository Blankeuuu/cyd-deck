#pragma once
#include <FS.h>
#include <SPI.h>
class FakeSD : public FS { public: bool begin(int,SPIClass&,int){return true;} uint64_t totalBytes(){return 1024ULL*1024*1024;} uint64_t usedBytes(){return 0;} };
extern FakeSD SD;
