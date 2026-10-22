#pragma once
#include <cstddef>
#include <cstdint>
#include <string>
using boolean = bool;
class String : public std::string { public: using std::string::string; };
class Stream { public: virtual ~Stream()=default; virtual std::size_t write(uint8_t)=0; virtual std::size_t write(const uint8_t*,std::size_t)=0; virtual int available()=0; virtual int read()=0; virtual int peek()=0; virtual void flush()=0; };
inline unsigned long millis() { static unsigned long value=0; return ++value; }
