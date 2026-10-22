#pragma once
#include <cstddef>
typedef struct { int unused; } mbedtls_sha256_context;
inline void mbedtls_sha256_init(mbedtls_sha256_context*){}
inline int mbedtls_sha256_starts_ret(mbedtls_sha256_context*,int){return 0;}
inline int mbedtls_sha256_update_ret(mbedtls_sha256_context*,const unsigned char*,std::size_t){return 0;}
inline int mbedtls_sha256_finish_ret(mbedtls_sha256_context*,unsigned char*){return 0;}
