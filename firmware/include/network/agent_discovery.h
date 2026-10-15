#pragma once

#include <cstdint>

namespace streamdeck::protocol {

enum class AgentEndpointSource {
  None,
  Manual,
  Mdns
};

struct AgentEndpointSelection {
  const char* host = "";
  uint16_t port = 0;
  AgentEndpointSource source = AgentEndpointSource::None;
  bool valid = false;

  AgentEndpointSelection() = default;
  AgentEndpointSelection(const char* selectedHost, uint16_t selectedPort,
      AgentEndpointSource selectedSource, bool isValid)
      : host(selectedHost), port(selectedPort), source(selectedSource), valid(isValid) {}
};

inline bool hasText(const char* value) {
  return value != nullptr && value[0] != '\0';
}

inline AgentEndpointSelection selectAgentEndpoint(
    const char* manualHost,
    uint16_t configuredPort,
    const char* mdnsHost,
    uint16_t mdnsPort) {
  if (hasText(mdnsHost) && mdnsPort != 0) {
    return AgentEndpointSelection(mdnsHost, mdnsPort, AgentEndpointSource::Mdns, true);
  }

  if (hasText(manualHost) && configuredPort != 0) {
    return AgentEndpointSelection(manualHost, configuredPort, AgentEndpointSource::Manual, true);
  }

  return {};
}

}  // namespace streamdeck::protocol
