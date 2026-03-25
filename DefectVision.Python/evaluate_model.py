"""
模型评估脚本
对比不同模型版本的精度指标，生成详细的每类评估报告
"""

import argparse
import json
from pathlib import Path
from typing import Optional


def evaluate(
    model_path: str,
    data_yaml: str,
    device: str = "0",
    imgsz: int = 1280,
    batch: int = 8,
    conf: float = 0.001,
    iou: float = 0.6,
    split: str = "val",
) -> dict:
    """
    评估 YOLO 模型

    Returns
    -------
    dict : 包含整体和每类指标
    """
    from ultralytics import YOLO

    model = YOLO(model_path)

    metrics = model.val(
        data=data_yaml,
        device=device,
        imgsz=imgsz,
        batch=batch,
        conf=conf,
        iou=iou,
        split=split,
        verbose=False,
    )

    # 整体指标
    result = {
        "model_path": model_path,
        "overall": {
            "map50": float(metrics.box.map50),
            "map5095": float(metrics.box.map),
            "precision": float(metrics.box.mp),
            "recall": float(metrics.box.mr),
        },
        "per_class": [],
    }

    # 每类指标
    names = model.names
    ap50_per_class = metrics.box.ap50
    ap_per_class = metrics.box.ap

    for i in range(len(names)):
        class_metrics = {
            "class_id": i,
            "class_name": names[i],
            "ap50": float(ap50_per_class[i]),
            "ap5095": float(ap_per_class[i]) if i < len(ap_per_class) else 0,
        }
        result["per_class"].append(class_metrics)

    return result


def compare_models(
    model_a_path: str,
    model_b_path: str,
    data_yaml: str,
    device: str = "0",
) -> dict:
    """
    对比两个模型的指标

    Returns
    -------
    dict : 包含两个模型的指标和差异
    """
    print(f"评估模型 A: {model_a_path}")
    result_a = evaluate(model_a_path, data_yaml, device)

    print(f"评估模型 B: {model_b_path}")
    result_b = evaluate(model_b_path, data_yaml, device)

    # 计算差异
    diff = {
        "map50": result_b["overall"]["map50"] - result_a["overall"]["map50"],
        "map5095": result_b["overall"]["map5095"] - result_a["overall"]["map5095"],
        "precision": result_b["overall"]["precision"] - result_a["overall"]["precision"],
        "recall": result_b["overall"]["recall"] - result_a["overall"]["recall"],
    }

    return {
        "model_a": result_a,
        "model_b": result_b,
        "diff": diff,
        "recommendation": "B" if diff["map50"] > 0 else "A",
    }


def main():
    parser = argparse.ArgumentParser(description="YOLO Model Evaluation")
    parser.add_argument("--model", required=True, help="模型路径")
    parser.add_argument("--data", required=True, help="数据集 YAML")
    parser.add_argument("--device", default="0")
    parser.add_argument("--imgsz", type=int, default=1280)
    parser.add_argument("--batch", type=int, default=8)
    parser.add_argument("--compare", default=None, help="对比模型路径")
    parser.add_argument("--output", default="eval_result.json")
    args = parser.parse_args()

    if args.compare:
        result = compare_models(args.model, args.compare, args.data, args.device)
        print(f"\n推荐使用: 模型 {result['recommendation']}")
    else:
        result = evaluate(
            args.model, args.data, args.device, args.imgsz, args.batch
        )
        o = result["overall"]
        print(f"\n整体指标:")
        print(f"  mAP@50:    {o['map50']:.4f}")
        print(f"  mAP@50-95: {o['map5095']:.4f}")
        print(f"  Precision: {o['precision']:.4f}")
        print(f"  Recall:    {o['recall']:.4f}")

        print(f"\n每类指标:")
        for c in result["per_class"]:
            print(f"  {c['class_name']:>15s}: AP50={c['ap50']:.4f}")

    Path(args.output).write_text(
        json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    print(f"\n结果已保存: {args.output}")


if __name__ == "__main__":
    main()