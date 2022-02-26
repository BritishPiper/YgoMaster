﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using YgoMasterClient;
using IL2CPP;
using System.Diagnostics;
using System.Windows.Forms;
using System.IO;
using System.Runtime.InteropServices;

namespace TMPro
{
    static unsafe class TMP_Text
    {
        static IL2Method methodGetText;
        static IL2Method methodSetText;

        static TMP_Text()
        {
            IL2Assembly assembly = Assembler.GetAssembly("Unity.TextMeshPro");
            IL2Class classInfo = assembly.GetClass("TMP_Text", "TMPro");
            methodGetText = classInfo.GetProperty("text").GetGetMethod();
            methodSetText = classInfo.GetProperty("text").GetSetMethod();
        }

        public static string GetText(IntPtr thisPtr)
        {
            IL2Object result = methodGetText.Invoke(thisPtr);
            return result != null ? result.GetValueObj<string>() : null;
        }

        public static void SetText(IntPtr thisPtr, string value)
        {
            methodSetText.Invoke(thisPtr, new IntPtr[] { new IL2String(value).ptr });
        }
    }
}

// Helper to display information about trunk in the title of the program (owned cards, filtered, etc)
// TODO: Create a text GameObject and use that instead. Could possibly be done by cloning the UR text, move it horizontally and update the text
namespace YgomGame.Deck
{
    static unsafe class CardCollectionView
    {
        static IL2Method GetDataList;
        delegate void Del_Start(IntPtr thisPtr);
        static Hook<Del_Start> hookStart;
        delegate void Del_UpdateView(IntPtr thisPtr, bool updateDataCount, bool select);
        static Hook<Del_UpdateView> hookUpdateView;

        static CardCollectionView()
        {
            IL2Assembly assembly = Assembler.GetAssembly("Assembly-CSharp");
            IL2Class classInfo = assembly.GetClass("CardCollectionView", "YgomGame.Deck");
            GetDataList = classInfo.GetProperty("m_DataList").GetGetMethod();
            hookStart = new Hook<Del_Start>(Start, classInfo.GetMethod("Start"));
            hookUpdateView = new Hook<Del_UpdateView>(UpdateView, classInfo.GetMethod("UpdateView"));
        }

        static void Start(IntPtr thisPtr)
        {
            hookStart.Original(thisPtr);
        }

        public static void UpdateView(IntPtr thisPtr, bool updateDataCount, bool select = true)
        {
            hookUpdateView.Original(thisPtr, updateDataCount, select);
            DeckEditViewController2.NumFilteredCards = new IL2List<object>(GetDataList.Invoke(thisPtr).ptr).Count;
            DeckEditViewController2.SetExtraText();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CardBaseData
    {
        public int CardID;
        public int PremiumID;
        public bool IsOwned;
        public int Obtained;
        public int Inventory;
        public int Rarity;
    }

    unsafe static class DeckView
    {
        public static bool DeckEditorDisableLimits;
        public static bool DeckEditorConvertStyleRarity;

        static IL2Class deckCardClassInfo;
        static IL2Class cardBaseDataClassInfo;
        static IL2Field fieldMainDeckCards;
        static IL2Field fieldMainCardDataList;
        static IL2Field fieldExtraDeckCards;
        static IL2Field fieldExtraCardDataList;
        static IL2Method methodSetDismantleMode;
        static IL2Method methodAddToMainDeckByID;
        static IL2Method methodAddToExtraDeckByID;
        static IL2Method methodRemoveCardFromMainOrExtra;
        static IL2Method methodSortMainDeckCards;
        static IL2Method methodSortExtraDeckCards;

        delegate int Del_GetAddableType(IntPtr thisPtr, int cardID, int regulation);
        static Hook<Del_GetAddableType> hookGetAddableType;

        static DeckView()
        {
            IL2Assembly assembly = Assembler.GetAssembly("Assembly-CSharp");
            IL2Class classInfo = assembly.GetClass("DeckView", "YgomGame.Deck");
            hookGetAddableType = new Hook<Del_GetAddableType>(GetAddableType, classInfo.GetMethod("GetAddableType"));
            methodSetDismantleMode = classInfo.GetMethod("SetDismantleMode");
            methodAddToMainDeckByID = classInfo.GetMethod("AddToMainDeckByID");
            methodAddToExtraDeckByID = classInfo.GetMethod("AddToExtraDeckByID");
            methodRemoveCardFromMainOrExtra = classInfo.GetMethod("RemoveCardFromMainOrExtra");
            methodSortMainDeckCards = classInfo.GetMethod("sortMainDeckCards");
            methodSortExtraDeckCards = classInfo.GetMethod("sortExtraDeckCards");

            deckCardClassInfo = assembly.GetClass("DeckCard", "YgomGame.Deck");
            cardBaseDataClassInfo = assembly.GetClass("CardBaseData", "YgomGame.Deck");
            fieldMainDeckCards = classInfo.GetField("mainDeckCards");
            fieldMainCardDataList = classInfo.GetField("mainCardDataList");
            fieldExtraDeckCards = classInfo.GetField("extraDeckCards");
            fieldExtraCardDataList = classInfo.GetField("extraCardDataList");
        }

        static int GetAddableType(IntPtr thisPtr, int cardID, int regulation)
        {
            if (DeckEditorDisableLimits)
            {
                return 0;// YgomGame.Deck.DeckView.AddableType.Addable
            }
            else
            {
                return hookGetAddableType.Original(thisPtr, cardID, regulation);
            }
        }

        public static void SetCards(IntPtr thisPtr, YgoMaster.CardCollection mainDeck, YgoMaster.CardCollection extraDeck)
        {
            // TODO: Look into ways of improving the performance of this

            bool b = false;
            methodSetDismantleMode.Invoke(thisPtr, new IntPtr[] { new IntPtr(&b) });

            IL2ListExplicit mainCardDataList = new IL2ListExplicit(fieldMainCardDataList.GetValue(thisPtr).ptr, cardBaseDataClassInfo);
            IL2ListExplicit extraCardDataList = new IL2ListExplicit(fieldExtraCardDataList.GetValue(thisPtr).ptr, cardBaseDataClassInfo);

            for (int i = 0; i < 2; i++)
            {
                IL2ListExplicit targetList = i == 0 ? mainCardDataList : extraCardDataList;
                int numCards = targetList.Count;
                for (int j = 0; j < numCards; j++)
                {
                    CardBaseData cardData = targetList.GetRef<CardBaseData>(0);
                    IntPtr removedCard = IntPtr.Zero;
                    methodRemoveCardFromMainOrExtra.Invoke(thisPtr, new IntPtr[]
                    {
                        new IntPtr(&cardData.CardID), new IntPtr(&cardData.PremiumID), new IntPtr(&removedCard)
                    });
                }
            }

            // Possibly not required? But GetRemainPremiumCard does stuff with CardCollectionInfo (possibly detached from the view)
            DeckEditViewController2.UpdateCollectionView(true, false);

            if (mainDeck != null && extraDeck != null)
            {
                IL2Dictionary<string, object> cardRare = null;
                IntPtr cardRarePtr = YgomSystem.Utility.ClientWork.GetByJsonPath("$.Master.CardRare");
                if (cardRarePtr != IntPtr.Zero)
                {
                    cardRare = new IL2Dictionary<string, object>(cardRarePtr);
                }

                for (int i = 0; i < 2; i++)
                {
                    IL2Method method = i == 0 ? methodAddToMainDeckByID : methodAddToExtraDeckByID;
                    List<KeyValuePair<int, YgoMaster.CardStyleRarity>> collection = i == 0 ? mainDeck.GetCollection() : extraDeck.GetCollection();
                    foreach (KeyValuePair<int, YgoMaster.CardStyleRarity> card in collection)
                    {
                        if (cardRare != null && !cardRare.ContainsKey(card.Key.ToString()))
                        {
                            continue;
                        }

                        int id = card.Key;
                        int prem = (int)card.Value;
                        bool owned = false;

                        if (DeckEditorConvertStyleRarity)
                        {
                            // Convert the style rarity (prem) based on owned cards
                            prem = (int)YgoMaster.CardStyleRarity.Normal;
                            owned = false;
                            while (prem <= (int)YgoMaster.CardStyleRarity.Royal && DeckEditViewController2.GetRemainPremiumCard(id, prem) > 0)
                            {
                                owned = true;
                                prem++;
                            }
                            if (owned)
                            {
                                prem--;
                            }
                            else
                            {
                                // No more cards in trunk with this card id, default to normal
                                prem = (int)YgoMaster.CardStyleRarity.Normal;
                            }
                        }
                        else
                        {
                            owned = DeckEditViewController2.GetRemainPremiumCard(id, prem) > 0;
                        }

                        int reg = 0;//?
                        bool sort = false;
                        bool isIni = false;//? isInitializeSelect?
                        bool noAdd = false;//?
                        method.Invoke(thisPtr, new IntPtr[]
                        {
                            new IntPtr(&id), new IntPtr(&prem), new IntPtr(&owned), new IntPtr(&reg), new IntPtr(&sort),
                            new IntPtr(&isIni), new IntPtr(&noAdd)
                        });
                    }
                }
                methodSortMainDeckCards.Invoke(thisPtr);
                methodSortExtraDeckCards.Invoke(thisPtr);
            }

            // Updates the used card counters in the card collection view
            DeckEditViewController2.UpdateCollectionView(true, false);
        }

        public static YgoMaster.DeckInfo GetDeckInfo(IntPtr thisPtr)
        {
            YgoMaster.DeckInfo result = new YgoMaster.DeckInfo();
            IL2ListExplicit mainCardDataList = new IL2ListExplicit(fieldMainCardDataList.GetValue(thisPtr).ptr, cardBaseDataClassInfo);
            IL2ListExplicit extraCardDataList = new IL2ListExplicit(fieldExtraCardDataList.GetValue(thisPtr).ptr, cardBaseDataClassInfo);
            for (int i = 0; i < 2; i++)
            {
                IL2ListExplicit targetList = i == 0 ? mainCardDataList : extraCardDataList;
                YgoMaster.CardCollection collection = i == 0 ? result.MainDeckCards : result.ExtraDeckCards;
                int numCards = targetList.Count;
                for (int j = 0; j < numCards; j++)
                {
                    CardBaseData cardData = targetList.GetRef<CardBaseData>(j);
                    collection.Add(cardData.CardID, (YgoMaster.CardStyleRarity)cardData.PremiumID);
                }
            }
            return result;
        }
    }
}

namespace YgomGame
{
    static unsafe class DeckEditViewController2
    {
        public static bool DeckEditorShowStats;
        public static int NumCards;
        public static int NumOwnedCards;
        public static int NumFilteredCards;

        static IntPtr currentInstance;
        static IL2Field fieldDeckView;
        static IL2Field fieldCollectionView;
        static IL2Method methodGetRemainPremiumCard;
        static IL2Method methodGetDeckName;
        static IL2Method methodGetDeckID;

        static IntPtr extraTextComponent;
        static IntPtr imageType;// UnityEngine.UI.Image
        static IntPtr textMeshType;// YgomSystem.YGomTMPro.ExtendedTextMeshProUGUI
        static IntPtr rectTransformType;// UnityEngine.CoreModule.RectTransform
        static IL2Method rectTransformSetAnchoredPosition;
        static IL2Method rectTransformSetOffsetMin;
        static IL2Method rectTransformSetSizeDelta;
        static IL2Method tmTextSetEnableWordWrapping;

        delegate void Del_NotificationStackRemove(IntPtr thisPtr);
        static Hook<Del_NotificationStackRemove> hookNotificationStackRemove;
        delegate void Del_InitializeView(IntPtr thisPtr);
        static Hook<Del_InitializeView> hookInitializeView;

        static DeckEditViewController2()
        {
            IL2Assembly assembly = Assembler.GetAssembly("Assembly-CSharp");
            IL2Class classInfo = assembly.GetClass("DeckEditViewController2", "YgomGame");
            fieldDeckView = classInfo.GetField("m_DeckView");
            fieldCollectionView = classInfo.GetField("m_CollectionView");
            methodGetRemainPremiumCard = classInfo.GetMethod("getRemainPremiumCard");
            methodGetDeckName = classInfo.GetProperty("m_DeckName").GetGetMethod();
            methodGetDeckID = classInfo.GetProperty("m_DeckID").GetGetMethod();
            hookInitializeView = new Hook<Del_InitializeView>(InitializeView, classInfo.GetMethod("InitializeView"));
            hookNotificationStackRemove = new Hook<Del_NotificationStackRemove>(NotificationStackRemove, classInfo.GetMethod("NotificationStackRemove"));

            imageType = CastUtils.IL2Typeof("Image", "UnityEngine.UI", "UnityEngine.UI");
            textMeshType = CastUtils.IL2Typeof("ExtendedTextMeshProUGUI", "YgomSystem.YGomTMPro", "Assembly-CSharp");
            rectTransformType = CastUtils.IL2Typeof("RectTransform", "UnityEngine", "UnityEngine.CoreModule");

            IL2Assembly coreModuleAssembly = Assembler.GetAssembly("UnityEngine.CoreModule");
            IL2Assembly uiAssembly = Assembler.GetAssembly("UnityEngine.UI");
            IL2Assembly textMeshAssembly = Assembler.GetAssembly("Unity.TextMeshPro");
            IL2Class rectTransformClassInfo = coreModuleAssembly.GetClass("RectTransform", "UnityEngine");
            rectTransformSetAnchoredPosition = rectTransformClassInfo.GetProperty("anchoredPosition").GetSetMethod();
            rectTransformSetOffsetMin = rectTransformClassInfo.GetProperty("offsetMin").GetSetMethod();
            rectTransformSetSizeDelta = rectTransformClassInfo.GetProperty("sizeDelta").GetSetMethod();
            IL2Class tmTextClassInfo = textMeshAssembly.GetClass("TMP_Text", "TMPro");
            tmTextSetEnableWordWrapping = tmTextClassInfo.GetProperty("enableWordWrapping").GetSetMethod();
        }

        static void InitializeView(IntPtr thisPtr)
        {
            hookInitializeView.Original(thisPtr);
            currentInstance = thisPtr;
            InitExtraTextComponent();
            //System.IO.File.WriteAllText("deck-dump.txt", UnityEngine.GameObject.Dump(UnityEngine.Component.GetGameObject(thisPtr)));
        }

        static void InitExtraTextComponent()
        {
            if (!DeckEditorShowStats)
            {
                return;
            }
            // TODO: Add error handling in the case that some of the details of this change after a patch
            IntPtr craftPointGroupObj = UnityEngine.GameObject.FindGameObjectByName(UnityEngine.Component.GetGameObject(currentInstance), "CraftPointGroup");
            IntPtr windowObj = UnityEngine.Component.GetGameObject(UnityEngine.Transform.GetParent(UnityEngine.GameObject.GetTranform(craftPointGroupObj)));

            IntPtr craftPointGroupObj2 = UnityEngine.UnityObject.Instantiate(craftPointGroupObj, UnityEngine.GameObject.GetTranform(windowObj));
            UnityEngine.UnityObject.Destroy(UnityEngine.GameObject.GetComponent(craftPointGroupObj2, imageType));// Get rid of the backgound

            // Get rid of unused objects
            UnityEngine.UnityObject.Destroy(UnityEngine.GameObject.FindGameObjectByName(craftPointGroupObj2, "CraftPointR"));
            UnityEngine.UnityObject.Destroy(UnityEngine.GameObject.FindGameObjectByName(craftPointGroupObj2, "CraftPointSR"));
            UnityEngine.UnityObject.Destroy(UnityEngine.GameObject.FindGameObjectByName(craftPointGroupObj2, "CraftPointUR"));

            IntPtr objCpN = UnityEngine.GameObject.FindGameObjectByName(craftPointGroupObj2, "CraftPointN");
            IntPtr objCpNIcon = UnityEngine.GameObject.FindGameObjectByName(objCpN, "Icon");
            IntPtr objCpNText = UnityEngine.GameObject.FindGameObjectByName(objCpN, "NumText");
            UnityEngine.UnityObject.Destroy(objCpNIcon);// Get rid of the icon

            extraTextComponent = UnityEngine.GameObject.GetComponent(objCpNText, textMeshType);

            // Disable word wrapping
            bool enableWordWrapping = false;
            tmTextSetEnableWordWrapping.Invoke(extraTextComponent, new IntPtr[] { new IntPtr(&enableWordWrapping) });

            // Increase the text area size
            IntPtr rectTransform = UnityEngine.GameObject.GetComponent(objCpNText, rectTransformType);
            YgoMasterClient.AssetHelper.Vector2 sizeDelta = new AssetHelper.Vector2(1000, 200);
            rectTransformSetSizeDelta.Invoke(rectTransform, new IntPtr[] { new IntPtr(&sizeDelta) });

            // Reposition the text (move it more to the left, it's currently positioned accounting for the icon)
            YgoMasterClient.AssetHelper.Vector2 anchorPos = new AssetHelper.Vector2(0, 0);
            rectTransformSetAnchoredPosition.Invoke(rectTransform, new IntPtr[] { new IntPtr(&anchorPos) });
            //YgoMasterClient.AssetHelper.Vector2 offsetMin = new AssetHelper.Vector2(0, -100);
            //rectTransformSetOffsetMin.Invoke(rectTransform, new IntPtr[] { new IntPtr(&offsetMin) });

            /*{x:52,y:0} - anchoredPosition
            {x:1,y:1} - anchorMax
            {x:0,y:0} - anchorMin
            {x:1052,y:100} - offsetMax
            {x:52,y:-100} - offsetMin
            {x:0,y:0.5} - pivot*/

            SetExtraText();
        }

        static void NotificationStackRemove(IntPtr thisPtr)
        {
            currentInstance = IntPtr.Zero;
            extraTextComponent = IntPtr.Zero;
            hookNotificationStackRemove.Original(thisPtr);
        }

        public static void SetExtraText()
        {
            if (!DeckEditorShowStats)
            {
                return;
            }
            IntPtr cardRarePtr = YgomSystem.Utility.ClientWork.GetByJsonPath("$.Master.CardRare");
            if (cardRarePtr != IntPtr.Zero)
            {
                IL2Dictionary<string, object> cardRare = new IL2Dictionary<string, object>(cardRarePtr);
                NumCards = cardRare.Count;
            }
            else
            {
                NumCards = 0;
            }
            IntPtr cardsHavePtr = YgomSystem.Utility.ClientWork.GetByJsonPath("$.Cards.have");
            if (cardsHavePtr != IntPtr.Zero)
            {
                IL2Dictionary<string, object> cardsHave = new IL2Dictionary<string, object>(cardsHavePtr);
                NumOwnedCards = cardsHave.Count;
            }
            else
            {
                NumOwnedCards = 0;
            }
            SetExtraText(NumOwnedCards + " / " + NumCards + " owned (" + NumFilteredCards + " filtered)");
        }

        public static void SetExtraText(string text)
        {
            if (DeckEditorShowStats && currentInstance != IntPtr.Zero && extraTextComponent != IntPtr.Zero)
            {
                TMPro.TMP_Text.SetText(extraTextComponent, text);
            }
        }

        public static void SetCards(YgoMaster.CardCollection mainDeck, YgoMaster.CardCollection extraDeck)
        {
            if (currentInstance == IntPtr.Zero)
            {
                return;
            }
            IntPtr deckView = fieldDeckView.GetValue(currentInstance).ptr;
            Deck.DeckView.SetCards(deckView, mainDeck, extraDeck);
        }

        public static YgoMaster.DeckInfo GetDeckInfo()
        {
            if (currentInstance == IntPtr.Zero)
            {
                return null;
            }
            IntPtr deckView = fieldDeckView.GetValue(currentInstance).ptr;
            return Deck.DeckView.GetDeckInfo(deckView);
        }

        public static int GetRemainPremiumCard(int id, int prem)
        {
            if (currentInstance == IntPtr.Zero)
            {
                return 0;
            }
            return methodGetRemainPremiumCard.Invoke(currentInstance, new IntPtr[] { new IntPtr(&id), new IntPtr(&prem) }).GetValueRef<int>();
        }

        public static void UpdateCollectionView(bool updateDataCount, bool select = true)
        {
            if (currentInstance == IntPtr.Zero)
            {
                return;
            }
            IntPtr collectionView = fieldCollectionView.GetValue(currentInstance).ptr;
            Deck.CardCollectionView.UpdateView(collectionView, updateDataCount, select);
        }

        public static string GetDeckName()
        {
            if (currentInstance == IntPtr.Zero)
            {
                return null;
            }
            IL2Object result = methodGetDeckName.Invoke(currentInstance);
            return result != null ? result.GetValueObj<string>() : null;
        }

        public static int GetDeckID()
        {
            if (currentInstance == IntPtr.Zero)
            {
                return 0;
            }
            IL2Object result = methodGetDeckName.Invoke(currentInstance);
            return result != null ? result.GetValueRef<int>() : 0;
        }
    }
}

namespace YgomGame.SubMenu
{
    unsafe static class DeckEditSubMenuViewController
    {
        static IL2Class classInfo;

        delegate void Del_OnCreatedView(IntPtr thisPtr);
        static Hook<Del_OnCreatedView> hookOnCreatedView;

        static DeckEditSubMenuViewController()
        {
            IL2Assembly assembly = Assembler.GetAssembly("Assembly-CSharp");
            classInfo = assembly.GetClass("DeckEditSubMenuViewController", "YgomGame.SubMenu");
            hookOnCreatedView = new Hook<Del_OnCreatedView>(OnCreatedView, classInfo.GetMethod("OnCreatedView"));
        }

        static void OnCreatedView(IntPtr thisPtr)
        {
            hookOnCreatedView.Original(thisPtr);
            SubMenuViewController.AddMenuItem(thisPtr, "From clipboard (YDKe / YDK / JSON)", OnLoadFromClipboard);
            SubMenuViewController.AddMenuItem(thisPtr, "To clipboard (YDKe)", OnSaveToClipboardYDKe);
            SubMenuViewController.AddMenuItem(thisPtr, "Load file", OnLoad);
            SubMenuViewController.AddMenuItem(thisPtr, "Save file", OnSave);
            SubMenuViewController.AddMenuItem(thisPtr, "Open decks folder", OnOpenDecksFolder);
            SubMenuViewController.AddMenuItem(thisPtr, "Clear deck", OnClear);
        }

        static void LoadDeckFromFile(string path)
        {
            if (File.Exists(path))
            {
                string extension = Path.GetExtension(path).ToLowerInvariant();
                switch (extension)
                {
                    case ".txt":
                    case ".ydk":
                    case ".json":
                        LoadDeckFromString(File.ReadAllText(path));
                        break;
                }
            }
        }

        static void LoadDeckFromString(string text)
        {
            try
            {
                if (string.IsNullOrEmpty(text))
                {
                    return;
                }
                YgoMaster.DeckInfo deck = new YgoMaster.DeckInfo();
                text = text.Trim().Trim('\r', '\n');
                if (text.StartsWith("ydke://", StringComparison.OrdinalIgnoreCase))
                {
                    string[] splitted = text.Substring(7).Split(new char[] { '!' }, StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 0; i < 3; i++)
                    {
                        if (i < splitted.Length && !string.IsNullOrEmpty(splitted[i]))
                        {
                            YgoMaster.CardCollection collection = null;
                            switch (i)
                            {
                                case 0: collection = deck.MainDeckCards; break;
                                case 1: collection = deck.ExtraDeckCards; break;
                                case 2: collection = deck.SideDeckCards; break;
                            }
                            byte[] buffer = Convert.FromBase64String(splitted[i]);
                            for (int j = 0; j < buffer.Length; j += 4)
                            {
                                int id = (int)YgoMaster.YdkHelper.GetOfficialId(BitConverter.ToInt32(buffer, j));
                                if (id >= 0)
                                {
                                    collection.Add(id);
                                }
                            }
                        }
                    }
                }
                else if (text.StartsWith("{") && text.Contains("\"m\""))
                {
                    deck.FromDictionaryEx(MiniJSON.Json.DeserializeStripped(text) as Dictionary<string, object>);
                }
                else if (text.Contains("#main"))
                {
                    YgoMaster.YdkHelper.LoadDeck(deck, text);
                }
                else
                {
                    YgomGame.Menu.CommonDialogViewController.OpenErrorDialog("Error", "Not a valid YDK / JSON file", "OK", null);
                    return;
                }
                if (deck.GetAllCards().Count > 0)
                {
                    YgomGame.DeckEditViewController2.SetCards(deck.MainDeckCards, deck.ExtraDeckCards);
                    PopViewController();
                }
            }
            catch (Exception e)
            {
                YgomGame.Menu.CommonDialogViewController.OpenConfirmationDialogScroll("Error", "An exception occurred\n" + e, "OK", null, null, true, 500);
            }
        }

        static Action OnLoad = () =>
        {
            // Duplicate of DuelStarter code...
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter =
                "JSON (*.json)|*.json" +
                "|YDK (*.ydk)|*.ydk" +
                "|JSON / YDK (*.json;*.ydk)|*.json;*.ydk" +
                "|All Files (*.*)|*.*";
            ofd.FilterIndex = 3;
            ofd.RestoreDirectory = true;
            if (ofd.ShowDialog() == DialogResult.OK && File.Exists(ofd.FileName))
            {
                LoadDeckFromFile(ofd.FileName);
            }
        };

        static Action OnLoadFromClipboard = () =>
        {
            if (Clipboard.ContainsFileDropList())
            {
                System.Collections.Specialized.StringCollection files = Clipboard.GetFileDropList();
                if (files.Count > 0)
                {
                    LoadDeckFromFile(files[0]);
                }
            }
            else if (Clipboard.ContainsText())
            {
                LoadDeckFromString(Clipboard.GetText());
            }
        };

        static Action OnSaveToClipboardYDKe = () =>
        {
            YgoMaster.DeckInfo deck = YgomGame.DeckEditViewController2.GetDeckInfo();
            if (deck != null && deck.GetAllCards().Count > 0)
            {
                List<byte[]> buffers = new List<byte[]>();
                for (int i = 0; i < 3; i++)
                {
                    YgoMaster.CardCollection collection = null;
                    switch (i)
                    {
                        case 0: collection = deck.MainDeckCards; break;
                        case 1: collection = deck.ExtraDeckCards; break;
                        case 2: collection = deck.SideDeckCards; break;
                    }
                    byte[] buffer = new byte[collection.Count * 4];
                    int index = 0;
                    foreach (KeyValuePair<int, YgoMaster.CardStyleRarity> card in collection.GetCollection())
                    {
                        // TODO: Handle cases where there's no YDK id for an official ID? (probably unlikely)
                        Buffer.BlockCopy(BitConverter.GetBytes((int)YgoMaster.YdkHelper.GetYdkId(card.Key)), 0, buffer, index, 4);
                        index += 4;
                    }
                    buffers.Add(buffer);
                }
                StringBuilder sb = new StringBuilder();
                sb.Append("ydke://");
                foreach (byte[] buffer in buffers)
                {
                    sb.Append(Convert.ToBase64String(buffer) + "!");
                }
                Clipboard.SetText(sb.ToString());
            }
        };

        static Action OnSave = () =>
        {
            try
            {
                YgoMaster.DeckInfo deck = DeckEditViewController2.GetDeckInfo();
                deck.Name = DeckEditViewController2.GetDeckName();
                int deckId = DeckEditViewController2.GetDeckID();
                if (deckId > 0)
                {
                    // Also see YgomGame.Deck.DeckInfo for other things we may want
                    deck.Accessory.FromDictionary(MiniJSON.Json.Deserialize(
                        YgomSystem.Utility.ClientWork.SerializePath("$.Deck.list." + deckId + ".accessory")) as Dictionary<string, object>);
                }
                if (deck.GetAllCards().Count > 0)
                {
                    SaveFileDialog sfd = new SaveFileDialog();
                    sfd.Filter =
                        "JSON (*.json)|*.json" +
                        "|YDK (*.ydk)|*.ydk";
                    sfd.FilterIndex = 2;
                    sfd.RestoreDirectory = true;
                    if (sfd.ShowDialog() == DialogResult.OK)
                    {
                        deck.File = sfd.FileName;
                        string extension = Path.GetExtension(sfd.FileName).ToLowerInvariant();
                        switch (extension)
                        {
                            case ".ydk":
                                YgoMaster.YdkHelper.SaveDeck(deck);
                                break;
                            case ".json":
                                File.WriteAllText(deck.File, MiniJSON.Json.Serialize(deck.ToDictionaryEx()));
                                break;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                YgomGame.Menu.CommonDialogViewController.OpenConfirmationDialogScroll("Error", "An exception occurred\n" + e, "OK", null, null, true, 500);
            }
        };

        static Action OnClear = () =>
        {
            YgomGame.Menu.CommonDialogViewController.OpenYesNoConfirmationDialog("Confirmation", "Are you sure you want to clear the deck?", OnClearConfirm);
        };
        static Action OnClearConfirm = () =>
        {
            YgomGame.DeckEditViewController2.SetCards(null, null);
            PopViewController();
        };

        static Action OnOpenDecksFolder = () =>
        {
            string path = Path.GetFullPath(Path.Combine(Program.DataDir, "Decks"));
            try
            {
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
            }
            catch
            {
            }
            if (Directory.Exists(path))
            {
                Process.Start("explorer", path);
            }
            else
            {
                YgomGame.Menu.CommonDialogViewController.OpenErrorDialog("Error", "Failed to find / create decks folder", "OK", null);
            }
        };

        static void PopViewController()
        {
            IntPtr manager = YgomSystem.Menu.ContentViewControllerManager.GetManager();
            IntPtr view = YgomSystem.UI.ViewControllerManager.GetViewController(manager, classInfo.IL2Typeof());
            if (view != IntPtr.Zero)
            {
                YgomSystem.UI.ViewControllerManager.PopChildViewController(manager, view);
            }
        }
    }

    unsafe static class SubMenuViewController
    {
        static IL2Method methodAddMenuItem;

        static SubMenuViewController()
        {
            IL2Assembly assembly = Assembler.GetAssembly("Assembly-CSharp");
            IL2Class classInfo = assembly.GetClass("SubMenuViewController", "YgomGame.SubMenu");
            methodAddMenuItem = classInfo.GetMethod("AddMenuItem");
        }

        public static void AddMenuItem(IntPtr thisPtr, string text, Action clickCallback, string onClickSL = null)
        {
            //IntPtr callback = DelegateHelper.DelegateIl2cppDelegate(clickCallback, clickCallback.GetType().GetClass());
            IntPtr callback = UnityEngine.Events._UnityAction.CreateUnityAction(clickCallback);
            methodAddMenuItem.Invoke(thisPtr, new IntPtr[] { new IL2String(text).ptr, callback, new IL2String(onClickSL).ptr });
        }
    }
}