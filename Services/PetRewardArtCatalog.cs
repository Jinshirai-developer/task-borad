namespace TaskApi.Services;

// Species-specific artwork; claiming a reward never grants a second choice on species changes.
public static class PetRewardArtCatalog
{
    public const int MaxRewardLevel = 5;
    private static readonly Dictionary<string, string[][]> Names = new(StringComparer.Ordinal)
    {
        ["dog"] =
        [
            ["おさんぽキャップ", "まいにちの蝶ネクタイ", "肉球キルトのマット"],
            ["冒険バケットハット", "アウトドアの結びリボン", "森のボルスタークッション"],
            ["小さなセーラー帽", "海風セーラーリボン", "ボーダーの舟形ベッド"],
            ["飛行士の耳あき帽", "空旅のスカーフリボン", "雲のふわふわクッション"],
            ["パレードの礼装帽", "金縁のお祝いリボン", "ロイヤル肉球ベッド"]
        ],
        ["cat"] =
        [
            ["空色のねこキャップ", "空色のちょうちょ結び", "おひるねキルト"],
            ["画家のベレー帽", "すみれのダブルリボン", "まあるいドーナツベッド"],
            ["木の葉の小さな帽子", "若葉のチェックリボン", "木の葉のクッション"],
            ["星読みのとんがり帽", "月と星のリボン", "三日月のクッション"],
            ["小さなねこ王冠", "金縁のベルベットリボン", "星のお祝いベッド"]
        ],
        ["rabbit"] =
        [
            ["若葉のちいさな帽子", "ミントの小花リボン", "若葉のふかふかマット"],
            ["クローバーの花冠", "クローバーの結びリボン", "クローバークッション"],
            ["ちいさな麦わら帽", "野いちごのリボン", "いちごのキルトベッド"],
            ["月あかりの小帽子", "月あかりのサテンリボン", "お月さまクッション"],
            ["春のお祝い花冠", "花びらのダブルリボン", "お花のお祝いベッド"]
        ],
        ["fox"] =
        [
            ["森番の小さな帽子", "森色の蝶ネクタイ", "苔色の木の葉マット"],
            ["野いちごベレー", "木いちごのリボン", "木いちごクッション"],
            ["羽根つき冒険帽", "紅葉の結びリボン", "紅葉のキルトベッド"],
            ["夜空のちいさな帽子", "夜露の星リボン", "夜の森のクッション"],
            ["金葉の小さな冠", "金葉のお祝いリボン", "森の王さまベッド"]
        ],
        ["panda"] =
        [
            ["笹のちいさな帽子", "笹色の蝶ネクタイ", "笹のキルトマット"],
            ["梅色ベレー帽", "梅の花リボン", "梅のふっくらクッション"],
            ["竹編みの小帽子", "竹林チェックリボン", "竹かご風クッション"],
            ["お茶会のミニ帽", "お茶会の絹リボン", "お茶会の座ぶとん"],
            ["笹の金冠", "金笹のお祝いリボン", "笹のお祝いベッド"]
        ],
        ["dragon"] =
        [
            ["若竜のちいさなキャップ", "若竜の小さなリボン", "うろこキルトのマット"],
            ["竜の冒険帽", "冒険者の結びリボン", "溶岩色のクッション"],
            ["空の飛行士帽", "空色の翼リボン", "雲の竜用ベッド"],
            ["月光のちいさな帽子", "結晶のサテンリボン", "月光のクッション"],
            ["太陽の小さな冠", "金縁の竜王リボン", "太陽のお祝いベッド"]
        ],
    };

    public static (string Name, string Image) Get(string species, int level, string kind)
    {
        var knownSpecies = Names.ContainsKey(species) ? species : "dog";
        if (level is < 1 or > MaxRewardLevel) throw new ArgumentOutOfRangeException(nameof(level));
        var index = kind switch { "hat" => 0, "bow" => 1, "mat" => 2, _ => throw new ArgumentOutOfRangeException(nameof(kind)) };
        return (Names[knownSpecies][level - 1][index], $"assets/pet/rewards-v2/{knownSpecies}/lv-{level}-{kind}.png");
    }
}
