﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RomUtilities;
using static System.Math;

namespace FF1Lib
{
	public enum ProgressiveScaleMode
	{
		Disabled,
		FiftyPercentAt12,
		FiftyPercentAt15,
		DoubledAt12,
		DoubledAt15,
		Progressive5Percent,
		Progressive10Percent,
		Progressive20Percent,
	}

	public partial class FF1Rom : NesRom
	{
		public const int PriceOffset = 0x37C00;
		public const int PriceSize = 2;
		public const int PriceCount = 240;

		// Scale is the geometric scale factor used with RNG.  Multiplier is where we make everything cheaper
		// instead of enemies giving more gold, so we don't overflow.
		public void ScalePrices(double scale, double multiplier, bool VanillaStartingGold, Blob[] text, MT19337 rng)
		{
            var prices = Get(PriceOffset, PriceSize * PriceCount).ToUShorts();
			for (int i = 0; i < prices.Length; i++)
			{
				prices[i] = (ushort)Min(Scale(prices[i] / multiplier, scale, 1, rng), 0xFFFF);
            }
            var questItemPrice = prices[(int)Item.Bottle];
            for (var i = 0; i < (int)Item.Tent; i++)
            {
                prices[i] = questItemPrice;
            }
			prices[(int)Item.WhiteShirt] = (ushort)(questItemPrice / 2);
			prices[(int)Item.BlackShirt] = (ushort)(questItemPrice / 2);
			prices[(int)Item.Ribbon] = questItemPrice;
            // Crystal can block Ship in early game where 50000 G would be too expensive
            prices[(int)Item.Crystal] = (ushort)(prices[(int)Item.Crystal] / 8);

			Put(PriceOffset, Blob.FromUShorts(prices));

			for (int i = GoldItemOffset; i < GoldItemOffset + GoldItemCount; i++)
			{
				text[i] = FF1Text.TextToBytes(prices[i].ToString() + " G");
			}

			var pointers = Get(ShopPointerOffset, ShopPointerCount * ShopPointerSize).ToUShorts();
			RepackShops(pointers);

			for (int i = (int)ShopType.Clinic; i < (int)ShopType.Inn + ShopSectionSize; i++)
			{
				if (pointers[i] != ShopNullPointer)
				{
					var priceBytes = Get(ShopPointerBase + pointers[i], 2);
					var priceValue = BitConverter.ToUInt16(priceBytes, 0);

					priceValue = (ushort)Scale(priceValue / multiplier, scale, 1, rng);
					priceBytes = BitConverter.GetBytes(priceValue);
					Put(ShopPointerBase + pointers[i], priceBytes);
				}
			}
			if (!VanillaStartingGold)
			{
				var startingGold = BitConverter.ToUInt16(Get(StartingGoldOffset, 2), 0);

				startingGold = (ushort)Min(Scale(startingGold / multiplier, scale, 1, rng), 0xFFFF);

				Put(StartingGoldOffset, BitConverter.GetBytes(startingGold));
			}
			
		}

		public void ScaleEnemyStats(double scale, MT19337 rng)
		{
			Enumerable.Range(0, EnemyCount).ToList().ForEach(index => ScaleSingleEnemyStats(index, scale, rng));
		}

		public void ScaleSingleEnemyStats(int index, double scale, MT19337 rng = null)
		{
			var enemy = Get(EnemyOffset + index * EnemySize, EnemySize);

			var hp = BitConverter.ToUInt16(enemy, 4);
			hp = (ushort)Min(Scale(hp, scale, 1.0, rng), 0x7FFF);
			var hpBytes = BitConverter.GetBytes(hp);
			Array.Copy(hpBytes, 0, enemy, 4, 2);

			enemy[6] = (byte)Min(Scale(enemy[6], scale, 0.25, rng), 0xFF); // morale
			enemy[8] = (byte)Min(Scale(enemy[8], scale, 1.0, rng), 0xF0); // evade clamped to 240
			enemy[9] = (byte)Min(Scale(enemy[9], scale, 0.5, rng), 0xFF); // defense
			enemy[10] = (byte)Max(Min(Scale(enemy[10], scale, 0.5, rng), 0xFF), 1); // hits
			enemy[11] = (byte)Min(Scale(enemy[11], scale, 1.0, rng), 0xFF); // hit%
			enemy[12] = (byte)Min(Scale(enemy[12], scale, 0.25, rng), 0xFF); // strength
			enemy[13] = (byte)Min(Scale(enemy[13], scale, 0.5, rng), 0xFF); // critical%

			Put(EnemyOffset + index * EnemySize, enemy);
		}

		private int Scale(double value, double scale, double adjustment, MT19337 rng = null)
		{
			double exponent = rng == null ? 1.0 : (double)rng.Next() / uint.MaxValue * 2.0 - 1.0;
			double adjustedScale = 1.0 + adjustment * (scale - 1.0);

			return (int)Round(Pow(adjustedScale, exponent) * value, MidpointRounding.AwayFromZero);
		}

		public void SetProgressiveScaleMode(ProgressiveScaleMode mode)
		{
			byte ScaleFactor = 1;   // Bonus given by progressive scaling in 1/n form (ScaleFactor = 5 means bonus is + 1/5 per item)
			byte Threshold = 0;		// Number of key items required for bonus.  Set this to 0 for progressive mode (every key item increases bonus)
			switch (mode)
			{
				case ProgressiveScaleMode.Disabled:
					return;
				case ProgressiveScaleMode.DoubledAt12:
					Threshold = 12;
					break;
				case ProgressiveScaleMode.DoubledAt15:
					Threshold = 15;
					break;
				case ProgressiveScaleMode.FiftyPercentAt12:
					Threshold = 12;
					ScaleFactor = 2;
					break;
				case ProgressiveScaleMode.FiftyPercentAt15:
					Threshold = 15;
					ScaleFactor = 2;
					break;
				case ProgressiveScaleMode.Progressive5Percent:
					ScaleFactor = 20;
					break;
				case ProgressiveScaleMode.Progressive10Percent:
					ScaleFactor = 10;
					break;
				case ProgressiveScaleMode.Progressive20Percent:
					ScaleFactor = 5;
					break;
			}

			//Progressive/Threshold scaling
			string HexBlob = $"200090ADB860D009A91C8580A960858160A9{ScaleFactor:X2}8516A9{Threshold:X2}8514F00EADB860C51490E6A9018515189005ADB8608515AD78688510AD79688511A516851220C090A515AAAD786865108D7868AD796865118D7968C9A7900AA90F8D7868A9A78D7968CAD0DF18AD76688510AD77688511A516851220C090A515AAAD766865108D7668AD776865118D7768C9A7900AA90F8D7668A9A78D7768CAD0DFAD76688588AD77688589A91C8580A960858160";
			PutInBank(0x0F, 0x9100, Blob.FromHex(HexBlob));
			//Inject into end-of-battle code
			PutInBank(0x0B, 0x9B4D, Blob.FromHex("20CBCFEAEAEAEAEA"));
		}

	}
}
