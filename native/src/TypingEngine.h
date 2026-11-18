#pragma once

#include <cstdint>
#include <deque>
#include <optional>
#include <random>
#include <string>
#include <vector>

namespace compact {

struct Options {
    double pace = 1.0;
    double rhythm = 0.5;
    double mistakeChance = 0.01;
};

enum class Action { Text, Left, Right, Backspace, Wait };

struct Step {
    Action action = Action::Wait;
    std::wstring text;
    int delayMs = 0;
    std::size_t highlightOffset = 0;
    std::size_t progress = 0;
    const wchar_t* activity = L"Typing";
};

// No Windows or UI dependencies. One step is scheduled at a time by the UI.
class TypingEngine {
public:
    TypingEngine(std::wstring text, Options options, std::uint32_t seed);
    std::optional<Step> next();
    std::size_t total() const { return source_.size(); }

private:
    struct Token { std::wstring text; std::size_t offset; };
    struct Correction { std::size_t position; std::size_t due; };
    std::wstring source_;
    Options options_;
    std::mt19937 random_;
    std::vector<Token> tokens_;
    std::vector<Correction> corrections_;
    std::deque<Step> queued_;
    std::size_t nextToken_ = 0;
    std::size_t caret_ = 0;
    std::size_t typedLength_ = 0;
    int groupRemaining_ = 0;
    double groupScale_ = 1;

    int between(int low, int high);
    double chance();
    int scaleDelay(int milliseconds);
    wchar_t nearby(wchar_t letter);
    std::size_t progress() const;
    void queueCorrection(const Correction& correction);
};

} // namespace compact
