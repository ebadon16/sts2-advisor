using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace QuestceSpire.GameBridge;

public static class GameStateReader
{
	private static readonly PropertyInfo _stateProperty = typeof(RunManager).GetProperty("State", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

	private static bool _keywordsFieldWarned;

	private static readonly object _stateLock = new object();

	private static IReadOnlyList<CardCreationResult> _lastCardOptionsField;
	private static IReadOnlyList<RelicModel> _lastRelicOptionsField;
	private static MerchantInventory _lastMerchantInventoryField;

	internal static IReadOnlyList<CardCreationResult> _lastCardOptions
	{
		get { lock (_stateLock) return _lastCardOptionsField; }
		set { lock (_stateLock) _lastCardOptionsField = value; }
	}

	internal static IReadOnlyList<RelicModel> _lastRelicOptions
	{
		get { lock (_stateLock) return _lastRelicOptionsField; }
		set { lock (_stateLock) _lastRelicOptionsField = value; }
	}

	internal static MerchantInventory _lastMerchantInventory
	{
		get { lock (_stateLock) return _lastMerchantInventoryField; }
		set { lock (_stateLock) _lastMerchantInventoryField = value; }
	}

	public static GameState ReadCurrentState()
	{
		try
		{
			RunManager instance = RunManager.Instance;
			if (instance == null)
			{
				Plugin.Log("RunManager is null — not in a run.");
				return null;
			}
			RunState runState = GetRunState(instance);
			if (runState == null)
			{
				Plugin.Log("RunState is null — not in a run.");
				return null;
			}
			Player player = runState.Players?.FirstOrDefault();
			if (player == null)
			{
				Plugin.Log("No player found in RunState.");
				return null;
			}
			var state = new GameState
			{
				Character = ReadCharacter(player),
				ActNumber = runState.CurrentActIndex + 1,
				Floor = runState.TotalFloor,
				CurrentHP = ReadCurrentHP(player),
				MaxHP = ReadMaxHP(player),
				Gold = ReadGold(player),
				AscensionLevel = runState.AscensionLevel,
				DeckCards = ReadDeck(player),
				CurrentRelics = ReadRelics(player),
				OfferedCards = ReadOfferedCards(),
				OfferedRelics = ReadOfferedRelics(),
				ShopCards = ReadShopCards(player),
				ShopRelics = ReadShopRelics(player)
			};
			// Read combat piles (best-effort)
			try
			{
				state.DrawPile = ReadPile(player, "DrawPile");
				state.DiscardPile = ReadPile(player, "DiscardPile");
				state.HandCards = ReadPile(player, "Hand");
			}
			catch (Exception pileEx)
			{
				Plugin.Log($"ReadPiles error (non-fatal): {pileEx.Message}");
			}
			return state;
		}
		catch (Exception ex)
		{
			Plugin.Log("Failed to read game state: " + ex.Message);
			return null;
		}
	}

	private static string ReadCharacter(Player player)
	{
		try
		{
			CharacterModel character = player.Character;
			if (character?.Id != null)
			{
				return character.Id.Entry?.ToLowerInvariant() ?? "unknown";
			}
			return "unknown";
		}
		catch (Exception ex)
		{
			Plugin.Log("ReadCharacter error: " + ex.Message);
			return "unknown";
		}
	}

	private static int ReadCurrentHP(Player player)
	{
		try
		{
			return player.Creature?.CurrentHp ?? 0;
		}
		catch
		{
			return 0;
		}
	}

	private static int ReadMaxHP(Player player)
	{
		try
		{
			return player.Creature?.MaxHp ?? 0;
		}
		catch
		{
			return 0;
		}
	}

	private static int ReadGold(Player player)
	{
		try
		{
			return player.Gold;
		}
		catch
		{
			return 0;
		}
	}

	private static List<CardInfo> ReadDeck(Player player)
	{
		List<CardInfo> list = new List<CardInfo>();
		try
		{
			CardPile deck = player.Deck;
			if (deck?.Cards == null)
			{
				return list;
			}
			foreach (CardModel card in deck.Cards)
			{
				list.Add(CardModelToInfo(card));
			}
		}
		catch (Exception ex)
		{
			Plugin.Log("ReadDeck error: " + ex.Message);
		}
		return list;
	}

	private static List<RelicInfo> ReadRelics(Player player)
	{
		List<RelicInfo> list = new List<RelicInfo>();
		try
		{
			IReadOnlyList<RelicModel> relics = player.Relics;
			if (relics == null)
			{
				return list;
			}
			foreach (RelicModel item in relics)
			{
				list.Add(RelicModelToInfo(item));
			}
		}
		catch (Exception ex)
		{
			Plugin.Log("ReadRelics error: " + ex.Message);
		}
		return list;
	}

	private static List<CardInfo> ReadOfferedCards()
	{
		List<CardInfo> list = new List<CardInfo>();
		try
		{
			if (_lastCardOptions != null)
			{
				foreach (CardCreationResult lastCardOption in _lastCardOptions)
				{
					CardModel card = lastCardOption.Card;
					if (card != null)
					{
						list.Add(CardModelToInfo(card));
					}
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.Log("ReadOfferedCards error: " + ex.Message);
		}
		return list;
	}

	private static List<RelicInfo> ReadOfferedRelics()
	{
		List<RelicInfo> list = new List<RelicInfo>();
		try
		{
			if (_lastRelicOptions != null)
			{
				foreach (RelicModel lastRelicOption in _lastRelicOptions)
				{
					list.Add(RelicModelToInfo(lastRelicOption));
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.Log("ReadOfferedRelics error: " + ex.Message);
		}
		return list;
	}

	private static List<CardInfo> ReadShopCards(Player player)
	{
		List<CardInfo> list = new List<CardInfo>();
		try
		{
			if (_lastMerchantInventory == null)
			{
				return list;
			}
			IEnumerable<MerchantCardEntry> characterCardEntries = _lastMerchantInventory.CharacterCardEntries;
			IEnumerable<MerchantCardEntry> first = characterCardEntries ?? Enumerable.Empty<MerchantCardEntry>();
			characterCardEntries = _lastMerchantInventory.ColorlessCardEntries;
			IEnumerable<MerchantCardEntry> second = characterCardEntries ?? Enumerable.Empty<MerchantCardEntry>();
			foreach (MerchantCardEntry item in first.Concat(second))
			{
				if (item.IsStocked)
				{
					CardModel cardModel = item.CreationResult?.Card;
					if (cardModel != null)
					{
						CardInfo info = CardModelToInfo(cardModel);
						info.Price = ReadMerchantPrice(item, player);
						list.Add(info);
					}
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.Log("ReadShopCards error: " + ex.Message);
		}
		return list;
	}

	private static List<RelicInfo> ReadShopRelics(Player player)
	{
		List<RelicInfo> list = new List<RelicInfo>();
		try
		{
			if (_lastMerchantInventory == null)
			{
				return list;
			}
			IReadOnlyList<MerchantRelicEntry> relicEntries = _lastMerchantInventory.RelicEntries;
			if (relicEntries == null)
			{
				return list;
			}
			foreach (MerchantRelicEntry item in relicEntries)
			{
				if (item.IsStocked)
				{
					RelicModel model = item.Model;
					if (model != null)
					{
						RelicInfo info = RelicModelToInfo(model);
						info.Price = ReadMerchantPrice(item, player);
						list.Add(info);
					}
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.Log("ReadShopRelics error: " + ex.Message);
		}
		return list;
	}

	private static List<CardInfo> ReadPile(Player player, string propertyName)
	{
		var list = new List<CardInfo>();
		try
		{
			// Try player.{prop} first, then player.Creature.{prop}
			object pile = null;
			var prop = player.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (prop != null) pile = prop.GetValue(player);
			if (pile == null && player.Creature != null)
			{
				prop = player.Creature.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (prop != null) pile = prop.GetValue(player.Creature);
			}
			if (pile is CardPile cardPile && cardPile.Cards != null)
			{
				foreach (CardModel card in cardPile.Cards)
				{
					list.Add(CardModelToInfo(card));
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.Log($"ReadPile({propertyName}) error (non-fatal): {ex.Message}");
		}
		return list;
	}

	private static RunState GetRunState(RunManager runManager)
	{
		return _stateProperty?.GetValue(runManager) as RunState;
	}

	internal static CardInfo CardModelToInfo(CardModel card)
	{
		CardInfo obj = new CardInfo
		{
			Id = (card.Id?.Entry ?? "unknown"),
			Name = (card.Title ?? card.Id?.Entry ?? "unknown"),
			Cost = (card.EnergyCost?.Canonical ?? 0),
			Type = card.Type.ToString(),
			Rarity = card.Rarity.ToString()
		};
		ModelId id = card.Id;
		obj.Upgraded = (object)id != null && id.Entry?.EndsWith("+") == true;
		obj.Tags = new List<string>();
		CardInfo cardInfo = obj;
		try
		{
			var field = card.GetType().GetField("_keywords", BindingFlags.Instance | BindingFlags.NonPublic);
			if (field == null)
			{
				if (!_keywordsFieldWarned)
				{
					Plugin.Log("WARN: CardModel._keywords field not found — card tags will be empty. Game version may have changed.");
					_keywordsFieldWarned = true;
				}
			}
			else if (field.GetValue(card) is HashSet<CardKeyword> hashSet)
			{
				foreach (CardKeyword item in hashSet)
				{
					if (item != CardKeyword.None)
					{
						cardInfo.Tags.Add(item.ToString().ToLowerInvariant());
					}
				}
			}
		}
		catch (System.Exception ex)
		{
			if (!_keywordsFieldWarned)
			{
				Plugin.Log($"WARN: Failed to read card keywords: {ex.Message}");
				_keywordsFieldWarned = true;
			}
		}
		return cardInfo;
	}

	private static int ReadMerchantPrice(object entry, Player player)
	{
		try
		{
			// MerchantCardEntry and MerchantRelicEntry both have GetPrice(Player)
			var method = entry.GetType().GetMethod("GetPrice", BindingFlags.Instance | BindingFlags.Public);
			if (method != null)
			{
				object result = method.Invoke(entry, new object[] { player });
				if (result is int price) return price;
			}
			// Fallback: try Price property
			var prop = entry.GetType().GetProperty("Price", BindingFlags.Instance | BindingFlags.Public);
			if (prop != null)
			{
				object result = prop.GetValue(entry);
				if (result is int price) return price;
			}
		}
		catch (Exception ex)
		{
			Plugin.Log($"ReadMerchantPrice error (non-fatal): {ex.Message}");
		}
		return 0;
	}

	private static RelicInfo RelicModelToInfo(RelicModel relic)
	{
		return new RelicInfo
		{
			Id = (relic.Id?.Entry ?? "unknown"),
			Name = (relic.Title?.ToString() ?? relic.Id?.Entry ?? "unknown"),
			Rarity = relic.Rarity.ToString()
		};
	}
}
