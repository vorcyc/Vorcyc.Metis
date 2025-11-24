namespace Vorcyc.Metis.Classifiers.Text;

using JiebaNet.Segmenter;
using Microsoft.VisualBasic.FileIO;
using System.Text.Json;
using System.Text.RegularExpressions;
using TorchSharp;
using TorchSharp.Modules;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;

/// <summary>
/// 语言选项（用于决定分词/预处理策略）。
/// </summary>
public enum Language
{
    /// <summary>
    /// 中文模式：依赖 Jieba 进行分词（需要 Resources\dict.txt）。
    /// </summary>
    Chinese,

    /// <summary>
    /// 英文模式：使用简单规则按非字母数字切分并转为小写，不依赖中文分词器。
    /// </summary>
    English
}

/// <summary>
/// 文本标题分类器（TorchSharp）— 支持中英双语，内置词表构建、训练、验证、预测与持久化。
/// </summary>
/// <remarks>
/// 核心功能：
/// - 数据加载：从 CSV 读取 (Category, Title)；
/// - 训练：基于可插拔分词（中文 Jieba / 英文规则），构建词表，训练 BiLSTM 分类器；
/// - 验证：自动划分验证集（默认20%），度量损失与准确率；
/// - 预测：对单条文本输出类别；
/// - 持久化：保存/加载模型权重与元数据（词表、标签映射、超参数、语言）。
///
/// 模型结构：Embedding + 双向 LSTM（batchFirst）+ 掩码均值池化 + Dropout + 线性分类头。
///
/// 设备选择：自动优先使用 CUDA（若可用），否则使用 CPU。
///
/// 依赖说明：
/// - 中文模式必须安装 JiebaNet.Segmenter，且确保 Resources\dict.txt 存在；
/// - 英文模式不依赖中文分词器。
///
/// 训练细节：
/// - 使用 CrossEntropyLoss；
/// - 动态序列长度：按当前训练数据分词后的最大长度与上限（默认 50）取较小值；
/// - 早停机制：验证集损失无改善若连续达到耐心值（默认 3），则提前停止；
/// - Dropout 仅在训练态生效，验证/预测前会切换至评估态（eval）。
///
/// 线程安全性：实例非线程安全；如需并发，请为每个线程/任务创建独立实例。
/// </remarks>
public class TextClassifier
{
    /// <summary>
    /// 内部模型：Embedding + BiLSTM + 掩码均值池化 + Dropout + Linear。
    /// </summary>
    private class TextClassifierModel : Module
    {
        // 嵌入层（padding_idx=0，PAD 向量恒为 0 且不更新）
        private readonly Embedding _embedding;
        // 双向 LSTM（numLayers=1，batchFirst=true）
        private readonly LSTM _lstm;
        // Dropout（仅训练时生效）
        private readonly Dropout _dropout;
        // 全连接分类头
        private readonly Linear _fc;

        /// <summary>
        /// 构造内部模型。
        /// </summary>
        /// <param name="vocabSize">词表大小（包含 &lt;PAD&gt; 与 &lt;UNK&gt;）</param>
        /// <param name="embedDim">词向量维度</param>
        /// <param name="hiddenDim">LSTM 单向隐藏维度（双向输出通道数为 2 * hiddenDim）</param>
        /// <param name="numClasses">类别数</param>
        /// <param name="dropout">分类头前的丢弃率（0~1）</param>
        public TextClassifierModel(int vocabSize, int embedDim, int hiddenDim, int numClasses, float dropout = 0.3f)
            : base("TextClassifierModel")
        {
            // 指定 padding_idx=0，保证 PAD 向量为 0 且不更新
            _embedding = nn.Embedding(vocabSize, embedDim, padding_idx: 0);
            // numLayers=1 时不需要 LSTM 内部 dropout
            _lstm = nn.LSTM(embedDim, hiddenDim, numLayers: 1, bidirectional: true, batchFirst: true);
            _dropout = nn.Dropout(dropout);
            _fc = nn.Linear(hiddenDim * 2, numClasses);
            RegisterComponents();
        }

        /// <summary>
        /// 前向传播。
        /// </summary>
        /// <param name="input">输入张量，形状 [B, T]，类型 Int64，PAD 位置为 0。</param>
        /// <returns>未归一化的分类 logits，形状 [B, C]。</returns>
        public torch.Tensor forward(torch.Tensor input)
        {
            // input: [B, T] (Int64), PAD==0
            var embeds = _embedding.forward(input);          // [B, T, E]
            var (lstmOut, _, _) = _lstm.forward(embeds);     // [B, T, 2H]

            // 按非 PAD 位置做掩码均值池化（避免短文本被 PAD 稀释）
            var mask = input.ne(0L).to_type(ScalarType.Float32).unsqueeze(-1); // [B, T, 1]
            var maskedOut = lstmOut * mask;                                    // [B, T, 2H]
            var sum = maskedOut.sum(1);                                        // [B, 2H]
            var lengths = mask.sum(1) + 1e-6f;                                 // [B, 1] 防止除 0
            var pooled = sum / lengths;                                        // [B, 2H]

            var dropped = _dropout.forward(pooled);
            var logits = _fc.forward(dropped);                                  // [B, C]
            return logits;
        }
    }

    // 训练/推理设备（CUDA 优先）
    private readonly Device _device;
    // 交叉熵损失
    private readonly Loss<torch.Tensor, torch.Tensor, torch.Tensor> _lossFn = nn.CrossEntropyLoss(reduction: Reduction.Mean);
    // 模型与优化器
    private TextClassifierModel? _model;
    private torch.optim.Optimizer? _optimizer;
    // 词表与标签映射
    private Dictionary<string, int>? _vocab;
    private Dictionary<string, int>? _labelToIndex;
    private Dictionary<long, string>? _indexToLabel;
    // 模型/数据维度
    private int _vocabSize;
    private readonly int _embedDim;
    private readonly int _hiddenDim;
    private int _numClasses;

    // 语言与分词器（仅中文使用 Jieba）
    private readonly Language _language;
    private readonly JiebaSegmenter? _segmenter; // 仅中文使用

    /// <summary>
    /// 当前分类器语言模式（只读）。用于分词与预处理策略的选择。
    /// </summary>
    public Language Language => _language;

    /// <summary>
    /// 初始化文本分类器实例。
    /// </summary>
    /// <param name="vocabSize">词表最大大小（实际可能小于该值，包含 &lt;PAD&gt; 和 &lt;UNK&gt;）</param>
    /// <param name="embedDim">词向量维度</param>
    /// <param name="hiddenDim">LSTM 单向隐藏维度（双向输出为 2*hiddenDim）</param>
    /// <param name="language">语言模式（Chinese/English）</param>
    /// <exception cref="FileNotFoundException">当中文模式且未找到 Resources\dict.txt 时抛出。</exception>
    public TextClassifier(int vocabSize = 10000, int embedDim = 128, int hiddenDim = 256, Language language = Language.Chinese)
    {
        _vocabSize = vocabSize;
        _embedDim = embedDim;
        _hiddenDim = hiddenDim;
        _device = cuda.is_available() ? torch.CUDA : torch.CPU;
        _language = language;

        if (_language == Language.Chinese)
        {
            _segmenter = new JiebaSegmenter();

            // 如有需要可添加领域词
            // _segmenter.AddWord("谢娜"); ...

            var dictPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "dict.txt");
            if (!File.Exists(dictPath))
                throw new FileNotFoundException($"Jieba dictionary file not found at: {dictPath}. Please copy dict.txt to Resources folder.");
        }
        else
        {
            _segmenter = null; // 英文模式不使用 Jieba
        }
    }

    /// <summary>
    /// 从 CSV 文件加载数据集（两列：Category,Title；默认首行为表头）。
    /// </summary>
    /// <param name="filePath">CSV 文件路径。</param>
    /// <returns>包含 (text, label) 的样本列表。</returns>
    /// <remarks>
    /// - 使用 __File > New__ 创建的标准 CSV 需确保以逗号分隔，文本字段建议用引号包裹；
    /// - 将跳过第一行表头；
    /// - 会忽略空白或不完整行。
    /// </remarks>
    /// <exception cref="FileNotFoundException">当文件不存在时可能由底层 API 抛出。</exception>
    public static List<(string text, string label)> LoadDataset(string filePath)
    {
        var dataset = new List<(string, string)>();
        using (var parser = new TextFieldParser(filePath))
        {
            parser.TextFieldType = FieldType.Delimited;
            parser.SetDelimiters(",");
            parser.HasFieldsEnclosedInQuotes = true;

            // 跳过标题行
            if (!parser.EndOfData) parser.ReadFields();

            while (!parser.EndOfData)
            {
                var fields = parser.ReadFields();
                if (fields != null && fields.Length >= 2)
                {
                    var category = fields[0].Trim();
                    var title = fields[1].Trim();
                    if (!string.IsNullOrWhiteSpace(category) && !string.IsNullOrWhiteSpace(title))
                        dataset.Add((title, category));
                }
            }
        }
        Console.WriteLine($"Loaded {dataset.Count} samples from {filePath}.");
        return dataset;
    }

    /// <summary>
    /// 训练分类器。
    /// </summary>
    /// <param name="dataset">训练数据集，元素为 (text, label)。</param>
    /// <param name="epochs">最大训练轮次（可能因早停提前结束）。</param>
    /// <param name="batchSize">批大小。</param>
    /// <param name="maxSeqLen">用户期望的最大序列长度（最终会与动态长度取较小值，默认上限 50）。</param>
    /// <param name="lr">学习率（Adam）。</param>
    /// <exception cref="ArgumentException">当数据条目少于 2 或标签种类少于 2 时抛出。</exception>
    /// <remarks>
    /// - 会随机打乱数据并按 80/20 切分训练/验证集；
    /// - 动态序列长度：基于训练集中最长分词结果与 50 取较小值，避免过度填充；
    /// - 采用 CrossEntropyLoss 与 Adam 优化器（含 L2 正则）； 
    /// - 每个 epoch 后在验证集评估，若验证损失未提升达耐心值（patience=3），则早停。
    /// </remarks>
    public void Train(List<(string text, string label)> dataset, int epochs = 10, int batchSize = 32, int maxSeqLen = 100, double lr = 1e-3)
    {
        if (dataset is null || dataset.Count < 2)
            throw new ArgumentException("Dataset must have at least 2 samples.");

        // 拆分训练/验证集（20% 验证）
        var shuffled = dataset.OrderBy(x => Guid.NewGuid()).ToList();
        int validSize = (int)(dataset.Count * 0.2);
        var trainData = shuffled.Take(dataset.Count - validSize).ToList();
        var validData = shuffled.Skip(dataset.Count - validSize).ToList();

        var texts = trainData.Select(d => d.text).ToList();
        var labelsText = trainData.Select(d => d.label).ToList();

        // 动态 maxSeqLen（按实际分词长度）
        int dynamicMaxSeqLen = texts.Select(t => Tokenize(t).Length).DefaultIfEmpty(50).Max();
        dynamicMaxSeqLen = Math.Min(dynamicMaxSeqLen, 50);
        Console.WriteLine($"Using dynamic maxSeqLen: {dynamicMaxSeqLen} (user specified: {maxSeqLen})");

        BuildVocabulary(texts, _vocabSize);
        BuildLabelMapping(labelsText);

        if (_model == null)
        {
            _model = new TextClassifierModel(_vocabSize, _embedDim, _hiddenDim, _numClasses, dropout: 0.3f).to(_device);
            _optimizer = torch.optim.Adam(_model.parameters(), lr: lr, weight_decay: 1e-5);
        }

        var labels = labelsText.Select(l => _labelToIndex![l]).ToList();

        _model.train();
        double bestValidLoss = double.MaxValue;
        int patience = 3;
        int noImprove = 0;

        for (int epoch = 0; epoch < epochs; epoch++)
        {
            double totalLoss = 0.0;
            int batches = (trainData.Count + batchSize - 1) / batchSize;

            for (int b = 0; b < batches; b++)
            {
                int start = b * batchSize;
                int end = Math.Min(start + batchSize, trainData.Count);

                var batchTexts = texts.GetRange(start, end - start);
                var batchLabels = labels.GetRange(start, end - start);

                var inputTensors = batchTexts.Select(t => TextToTensor(t, dynamicMaxSeqLen)).ToArray();
                var inputs = cat(inputTensors, dim: 0);
                var targets = tensor(batchLabels.Select(l => (long)l).ToArray(), dtype: ScalarType.Int64, device: _device);

                _optimizer!.zero_grad();
                var logits = _model.forward(inputs);
                var loss = _lossFn.forward(logits, targets);
                loss.backward();
                _optimizer.step();

                totalLoss += loss.item<float>();

                // 释放批内临时张量
                foreach (var t in inputTensors) t.Dispose();
                inputs.Dispose();
                targets.Dispose();
                loss.Dispose();
                logits.Dispose();
            }

            // 验证（关闭 Dropout/BN）
            _model.eval();
            var (validLoss, validAcc) = Evaluate(validData, dynamicMaxSeqLen);
            _model.train();

            double avgTrainLoss = totalLoss / batches;

            Console.WriteLine($"Epoch {epoch + 1}/{epochs}, Train Loss: {avgTrainLoss:0.####}, Valid Loss: {validLoss:0.####}, Valid Acc: {validAcc:0.##%}");

            // 早停
            if (validLoss < bestValidLoss)
            {
                bestValidLoss = validLoss;
                noImprove = 0;
            }
            else
            {
                noImprove++;
                if (noImprove >= patience)
                {
                    Console.WriteLine($"Early stopping at epoch {epoch + 1}. Best valid loss: {bestValidLoss:0.####}");
                    break;
                }
            }
        }
        _model.eval();
    }

    /// <summary>
    /// 在验证集上评估损失与准确率（不计算梯度）。
    /// </summary>
    /// <param name="validData">验证数据集。</param>
    /// <param name="maxSeqLen">序列最大长度（与训练时保持一致）。</param>
    /// <returns>(平均损失, 准确率)。</returns>
    private (double loss, double acc) Evaluate(List<(string text, string label)> validData, int maxSeqLen)
    {
        double totalLoss = 0.0;
        int correct = 0, total = validData.Count;

        using var noGrad = no_grad();
        foreach (var (text, label) in validData)
        {
            var input = TextToTensor(text, maxSeqLen);
            var targetIdx = _labelToIndex![label];
            var target = tensor(new long[] { targetIdx }, dtype: ScalarType.Int64, device: _device);

            var logits = _model!.forward(input);
            var loss = _lossFn.forward(logits, target);
            totalLoss += loss.item<float>();

            // 直接对 logits 取 argmax（无需 softmax）
            var predictedIdx = argmax(logits, dim: -1).item<long>();
            if (predictedIdx == targetIdx) correct++;

            input.Dispose();
            target.Dispose();
            logits.Dispose();
            loss.Dispose();
        }

        return (totalLoss / total, (double)correct / total);
    }

    /// <summary>
    /// 对单条文本进行预测。
    /// </summary>
    /// <param name="text">输入文本。</param>
    /// <param name="maxSeqLen">序列最大长度（建议与训练时动态长度一致，默认 100 仅作兜底）。</param>
    /// <returns>预测的标签文本。</returns>
    /// <exception cref="InvalidOperationException">当模型或元数据未初始化（未训练/未加载）时抛出。</exception>
    public string Predict(string text, int maxSeqLen = 100)
    {
        EnsureReadyForInference();
        using var noGrad = no_grad();
        var input = TextToTensor(text, maxSeqLen);
        var logits = _model!.forward(input);
        var probs = nn.functional.softmax(logits, dim: -1);
        var predictedIndex = argmax(probs, dim: -1).item<long>();

        input.Dispose();
        logits.Dispose();
        probs.Dispose();

        return _indexToLabel![predictedIndex];
    }

    /// <summary>
    /// 保存模型权重与元数据（同名 .meta.json）。
    /// </summary>
    /// <param name="modelPath">模型文件保存路径（例如：models/title_cls.pt）。</param>
    /// <exception cref="ArgumentException">当路径为空或空白时抛出。</exception>
    /// <exception cref="InvalidOperationException">当模型或元数据未就绪时抛出。</exception>
    /// <remarks>
    /// 将生成两个文件：
    /// - 模型权重：modelPath（TorchSharp 保存格式）； 
    /// - 元数据：将 modelPath 扩展名替换为 .meta.json，包含词表、标签映射、超参数与语言。
    /// </remarks>
    public void Save(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
            throw new ArgumentException("modelPath is required.");
        EnsureReadyForInference();

        var dir = Path.GetDirectoryName(Path.GetFullPath(modelPath));
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir!);

        _model!.save(modelPath);

        var meta = new ModelMetadata
        {
            VocabSize = _vocabSize,
            EmbedDim = _embedDim,
            HiddenDim = _hiddenDim,
            NumClasses = _numClasses,
            Vocab = _vocab!,
            LabelToIndex = _labelToIndex!,
            Language = _language
        };

        var metaPath = Path.ChangeExtension(modelPath, ".meta.json");
        var json = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(metaPath, json);
    }

    /// <summary>
    /// 从磁盘加载已保存的模型与元数据。
    /// </summary>
    /// <param name="modelPath">模型文件路径（需有同名 .meta.json）。</param>
    /// <returns>可直接用于预测的 <see cref="TextClassifier"/> 实例。</returns>
    /// <exception cref="ArgumentException">当路径为空或空白时抛出。</exception>
    /// <exception cref="FileNotFoundException">当模型文件或元数据文件不存在时抛出。</exception>
    /// <exception cref="InvalidOperationException">当元数据解析失败时抛出。</exception>
    public static TextClassifier Load(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
            throw new ArgumentException("modelPath is required.");
        var metaPath = Path.ChangeExtension(modelPath, ".meta.json");
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"Model file not found: {modelPath}");
        if (!File.Exists(metaPath))
            throw new FileNotFoundException($"Metadata file not found: {metaPath}");

        var meta = JsonSerializer.Deserialize<ModelMetadata>(File.ReadAllText(metaPath))
                   ?? throw new InvalidOperationException("Failed to parse model metadata.");

        var classifier = new TextClassifier(meta.VocabSize, meta.EmbedDim, meta.HiddenDim, meta.Language)
        {
            _vocab = meta.Vocab,
            _labelToIndex = meta.LabelToIndex,
            _indexToLabel = meta.LabelToIndex.ToDictionary(kvp => (long)kvp.Value, kvp => kvp.Key),
            _numClasses = meta.NumClasses
        };

        classifier._model = new TextClassifierModel(meta.VocabSize, meta.EmbedDim, meta.HiddenDim, meta.NumClasses)
            .to(classifier._device);
        classifier._model.load(modelPath);
        classifier._model.eval();

        return classifier;
    }

    /// <summary>
    /// 确保模型、词表与标签映射均已就绪（未就绪则抛出异常）。
    /// </summary>
    /// <exception cref="InvalidOperationException">当任何必要组件未初始化时抛出。</exception>
    private void EnsureReadyForInference()
    {
        if (_model == null) throw new InvalidOperationException("Model is not initialized. Train or Load a model first.");
        if (_vocab == null) throw new InvalidOperationException("Vocabulary is not initialized. Train or Load a model first.");
        if (_labelToIndex == null || _indexToLabel == null) throw new InvalidOperationException("Label mappings are not initialized. Train or Load a model first.");
    }

    /// <summary>
    /// 基于训练数据的标签列表构建标签到索引的映射。
    /// </summary>
    /// <param name="textLabels">训练集中的标签文本列表。</param>
    /// <exception cref="ArgumentException">当标签种类少于 2 时抛出。</exception>
    private void BuildLabelMapping(List<string> textLabels)
    {
        var unique = textLabels.Distinct().ToList();
        _numClasses = unique.Count;
        if (_numClasses < 2) throw new ArgumentException("At least 2 unique labels are required.");

        _labelToIndex = new Dictionary<string, int>();
        _indexToLabel = new Dictionary<long, string>();
        for (int i = 0; i < unique.Count; i++)
        {
            _labelToIndex[unique[i]] = i;
            _indexToLabel[i] = unique[i];
        }
    }

    /// <summary>
    /// 构建词表（频次降序截断，保留特殊符号 &lt;PAD&gt; 和 &lt;UNK&gt;）。
    /// </summary>
    /// <param name="texts">训练文本集合。</param>
    /// <param name="maxVocabSize">词表最大容量（包含 2 个特殊符号）。</param>
    private void BuildVocabulary(List<string> texts, int maxVocabSize)
    {
        var wordCounts = new Dictionary<string, int>();
        foreach (var text in texts)
        {
            foreach (var word in Tokenize(text))
            {
                if (!wordCounts.ContainsKey(word)) wordCounts[word] = 0;
                wordCounts[word]++;
            }
        }

        var sortedWords = wordCounts
            .OrderByDescending(kv => kv.Value)
            .Take(Math.Max(0, maxVocabSize - 2))
            .Select(kv => kv.Key)
            .ToList();

        _vocab = new Dictionary<string, int>
        {
            ["<PAD>"] = 0,
            ["<UNK>"] = 1
        };

        for (int i = 0; i < sortedWords.Count; i++)
            _vocab[sortedWords[i]] = i + 2;

        _vocabSize = _vocab.Count;
    }

    /// <summary>
    /// 文本分词/切词。
    /// </summary>
    /// <param name="text">原始文本。</param>
    /// <returns>分词后的 token 数组。若英文文本无有效 token，返回 ["&lt;UNK&gt;"]。</returns>
    private string[] Tokenize(string text)
    {
        if (_language == Language.Chinese)
        {
            // 依赖 Jieba 进行中文分词
            return _segmenter!.Cut(text).ToArray();
        }
        else
        {
            // 英文/数字等：按非字母数字切分并转小写
            var lowered = text.ToLowerInvariant();
            var tokens = Regex.Split(lowered, @"[^a-z0-9]+")
                              .Where(s => s.Length > 0)
                              .ToArray();
            return tokens.Length > 0 ? tokens : new[] { "<UNK>" };
        }
    }

    /// <summary>
    /// 将文本转为张量表示（填充/截断至固定长度）。
    /// </summary>
    /// <param name="text">输入文本。</param>
    /// <param name="maxLength">最大序列长度（不足则以 &lt;PAD&gt; 补齐，超长则截断）。</param>
    /// <returns>形状为 [1, maxLength] 的 Int64 张量，位于当前设备。</returns>
    /// <exception cref="InvalidOperationException">当词表未初始化时抛出。</exception>
    private torch.Tensor TextToTensor(string text, int maxLength)
    {
        if (_vocab == null) throw new InvalidOperationException("Vocabulary not initialized.");
        var words = Tokenize(text);
        var ids = new List<long>(capacity: Math.Max(maxLength, words.Length));

        foreach (var w in words)
            ids.Add(_vocab.TryGetValue(w, out int id) ? id : _vocab["<UNK>"]);

        if (ids.Count > maxLength) ids = ids.Take(maxLength).ToList();
        while (ids.Count < maxLength) ids.Add(_vocab["<PAD>"]);

        return tensor(ids.ToArray(), dtype: ScalarType.Int64, device: _device).unsqueeze(0);
    }

    /// <summary>
    /// 模型元数据（用于持久化与重建分类器状态）。
    /// </summary>
    private sealed class ModelMetadata
    {
        /// <summary>
        /// 词表大小。
        /// </summary>
        public int VocabSize { get; set; }

        /// <summary>
        /// 词向量维度。
        /// </summary>
        public int EmbedDim { get; set; }

        /// <summary>
        /// LSTM 单向隐藏维度。
        /// </summary>
        public int HiddenDim { get; set; }

        /// <summary>
        /// 类别数。
        /// </summary>
        public int NumClasses { get; set; }

        /// <summary>
        /// 词表映射（token -> id）。
        /// </summary>
        public Dictionary<string, int> Vocab { get; set; } = new();

        /// <summary>
        /// 标签到索引的映射（label -> id）。
        /// </summary>
        public Dictionary<string, int> LabelToIndex { get; set; } = new();

        /// <summary>
        /// 训练时的语言模式。用于在加载模型时恢复分词/预处理策略。
        /// 缺省为 Chinese 以兼容旧版本元数据（未包含该字段时）。
        /// </summary>
        public Language Language { get; set; } = Language.Chinese;
    }
}