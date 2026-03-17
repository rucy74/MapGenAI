# MapGen AI

![Preview](dist/About/Preview.png)

**Describe your map in natural language — AI generates it for you.**

A RimWorld mod that replaces manual UI sliders with an AI chat interface. Type anything like *"mountain fortress with hot springs"*, *"straight river on the left side"*, or *"just surprise me"* and watch the AI configure your map in real-time with Map Preview.

> This mod was 100% built by [Claude Code](https://claude.ai/claude-code) (AI coding agent). The author has zero C# experience — every line of code, every Harmony patch, and every UI element was written by AI through natural language conversation.

## Features

- **Natural Language Map Generation** — Describe terrain in plain text, AI converts it to map parameters
- **Live Map Preview** — See changes instantly through Map Preview integration
- **Elevation Shapes** — Diagonal mountain ranges, central lakes, ring fortresses, canyons, slopes, and more
- **River Control** — Direction, position, and straight river mode
- **Terrain Tuning** — Rich soil density, vegetation, animals, ore, ruins, rock types, caves, geysers
- **Odyssey DLC Support** — 60+ tile mutators (hot springs, fjords, oasis, animal habitats, etc.)
- **Preset System** — Save and load your favorite map configurations
- **Iterative Refinement** — Keep chatting to tweak your map until it's perfect
- **Undo & Reset** — Made a wrong turn? **Undo** reverts to before your last message. **Reset** restores the tile to its original state. Both are one click.
- **Korean / English / Japanese / Chinese (Simplified)** — Full multilingual UI and AI responses

## Quick Start

1. Install this mod + [Map Preview](https://steamcommunity.com/sharedfiles/filedetails/?id=2800857642) (required)
2. Open **Mod Settings → MapGen AI**, select your LLM provider and enter your API key
3. On the world map, select a tile — click the **✦ AI Map Gen** button next to Map Preview
4. Describe your ideal map and hit Send!

## Supported LLM Providers

| Provider | Notes |
|----------|-------|
| Google Gemini | Free tier available |
| OpenAI | GPT-4o, etc. |
| Local LLMs | Ollama, LM Studio, or any OpenAI-compatible API |

## Example Prompts

- *"Diagonal mountain range with a central lake"*
- *"Mountain fortress with hot springs"*
- *"Straight river on the left, marble only, no rock chunks"*
- *"Clean flatland with lots of rich soil"*
- *"Just recommend something"*

## Tips

- **Describe everything in one message** — For best results, put all your requirements in a single prompt rather than adding them one by one. LLMs perform significantly better with single comprehensive instructions ([research shows up to 39% accuracy drop in multi-turn conversations](https://arxiv.org/abs/2505.06120)).
- If you do need to tweak, the AI will try to preserve your previous settings — but a fresh single prompt is always more reliable.
- **Not sure what features are available?** Just ask! Type things like *"what ancient ruins are there?"*, *"what special terrain features can I add here?"*, or *"what rock types are available?"* and the AI will list the options for your current tile.
- **Undo and Reset are your safety net** — If the AI generates something you don't like, hit **Undo** to go back one step, or **Reset** to wipe everything and start fresh.

## Requirements

- [Map Preview](https://steamcommunity.com/sharedfiles/filedetails/?id=2800857642) (required dependency)
- An API key from Gemini, OpenAI, or a local LLM server

## Install (non-Steam)

1. Download the latest zip from [Releases](https://github.com/rucy74/MapGenAI/releases)
2. Extract to your `RimWorld/Mods/` folder
3. Enable in mod list

## Compatibility

- RimWorld 1.5 / 1.6
- Odyssey DLC — Supported (enables 60+ additional terrain mutators)

## Project Structure

```
dev/            — Full mod + source (development)
  About/        — Mod metadata + preview image
  Assemblies/   — Compiled DLL (build output)
  Defs/         — XML definitions
  Languages/    — Translations (Korean / English / Japanese / Chinese Simplified)
  Source/       — C# source code
dist/           — Release-ready (copy to RimWorld/Mods/)
docs/           — Dev logs, workshop description, prompt engineering notes
```

## License

MIT
