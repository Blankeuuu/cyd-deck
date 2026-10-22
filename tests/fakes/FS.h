#pragma once
#include <Arduino.h>
#include <map>
#include <memory>
#include <string>
#include <vector>
#define FILE_READ "r"
#define FILE_WRITE "w"
class FakeFsState;
class File : public Stream {
 public:
  File() = default;
  File(std::shared_ptr<FakeFsState> state, std::string path, bool write, bool dir = false);
  std::size_t write(uint8_t value) override;
  std::size_t write(const uint8_t* data, std::size_t length) override;
  int available() override;
  int read() override;
  int peek() override;
  void flush() override {}
  std::size_t size() const;
  void close() {}
  operator bool() const { return static_cast<bool>(state_); }
  std::size_t readBytesUntil(char end, char* buffer, std::size_t capacity);
  int printf(const char* format, ...);
  bool isDirectory() const { return dir_; }
  File openNextFile(const char* = FILE_READ) { return {}; }
  const char* name() const { return path_.c_str(); }
 private:
  std::shared_ptr<FakeFsState> state_;
  std::string path_;
  std::size_t pos_ = 0;
  bool write_ = false;
  bool dir_ = false;
};
class FS {
 public:
  FS();
  File open(const char* path, const char* mode = FILE_READ, const bool create = false);
  bool exists(const char* path);
  bool remove(const char* path);
  bool rename(const char* from, const char* to);
  bool mkdir(const char* path);
  bool failRemove = false;
 private:
  std::shared_ptr<FakeFsState> state_;
};
