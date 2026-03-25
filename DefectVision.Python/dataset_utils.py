"""
数据集工具
- YOLO ↔ COCO 格式转换
- 数据集统计分析
- 数据集拆分
- 数据增强预览
"""

import json
import os
import random
import shutil
from collections import Counter, defaultdict
from pathlib import Path
from typing import Dict, List, Optional, Tuple


def analyze_dataset(data_yaml_path: str) -> dict:
    """
    分析 YOLO 数据集的统计信息

    Returns
    -------
    dict : 数据集统计
    """
    import yaml

    with open(data_yaml_path, "r", encoding="utf-8") as f:
        data_cfg = yaml.safe_load(f)

    base_path = Path(data_cfg.get("path", Path(data_yaml_path).parent))
    names = data_cfg.get("names", {})

    stats = {
        "num_classes": len(names),
        "class_names": names,
        "splits": {},
    }

    for split in ["train", "val", "test"]:
        split_key = split
        if split_key not in data_cfg:
            continue

        img_dir = base_path / data_cfg[split_key]
        lbl_dir = Path(str(img_dir).replace("images", "labels"))

        if not img_dir.exists():
            continue

        image_files = list(img_dir.glob("*"))
        image_exts = {".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff"}
        image_files = [f for f in image_files if f.suffix.lower() in image_exts]

        class_counts = Counter()
        bbox_counts = []
        images_with_no_labels = 0
        total_bboxes = 0

        for img_file in image_files:
            lbl_file = lbl_dir / (img_file.stem + ".txt")

            if not lbl_file.exists():
                images_with_no_labels += 1
                bbox_counts.append(0)
                continue

            lines = lbl_file.read_text().strip().split("\n")
            lines = [l for l in lines if l.strip()]

            bbox_counts.append(len(lines))
            total_bboxes += len(lines)

            for line in lines:
                parts = line.strip().split()
                if parts:
                    cls_id = int(parts[0])
                    cls_name = names.get(cls_id, f"class_{cls_id}")
                    class_counts[cls_name] += 1

        stats["splits"][split] = {
            "num_images": len(image_files),
            "num_labeled": len(image_files) - images_with_no_labels,
            "num_unlabeled": images_with_no_labels,
            "total_bboxes": total_bboxes,
            "avg_bboxes_per_image": total_bboxes / max(len(image_files), 1),
            "max_bboxes_per_image": max(bbox_counts) if bbox_counts else 0,
            "class_distribution": dict(class_counts.most_common()),
        }

    return stats


def yolo_to_coco(
    images_dir: str,
    labels_dir: str,
    class_names: Dict[int, str],
    output_json: str,
) -> None:
    """
    YOLO 格式标注转 COCO JSON 格式
    """
    from PIL import Image

    coco = {
        "images": [],
        "annotations": [],
        "categories": [],
    }

    # 类别
    for cls_id, cls_name in class_names.items():
        coco["categories"].append({
            "id": cls_id,
            "name": cls_name,
            "supercategory": "defect",
        })

    image_exts = {".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff"}
    ann_id = 1

    images_path = Path(images_dir)
    labels_path = Path(labels_dir)

    for img_id, img_file in enumerate(sorted(images_path.iterdir()), start=1):
        if img_file.suffix.lower() not in image_exts:
            continue

        # 获取图片尺寸
        with Image.open(img_file) as img:
            w, h = img.size

        coco["images"].append({
            "id": img_id,
            "file_name": img_file.name,
            "width": w,
            "height": h,
        })

        # 读取标签
        lbl_file = labels_path / (img_file.stem + ".txt")
        if not lbl_file.exists():
            continue

        for line in lbl_file.read_text().strip().split("\n"):
            parts = line.strip().split()
            if len(parts) < 5:
                continue

            cls_id = int(parts[0])
            cx, cy, bw, bh = map(float, parts[1:5])

            # YOLO 归一化 → COCO 像素坐标
            x = (cx - bw / 2) * w
            y = (cy - bh / 2) * h
            box_w = bw * w
            box_h = bh * h

            coco["annotations"].append({
                "id": ann_id,
                "image_id": img_id,
                "category_id": cls_id,
                "bbox": [round(x, 2), round(y, 2), round(box_w, 2), round(box_h, 2)],
                "area": round(box_w * box_h, 2),
                "iscrowd": 0,
            })
            ann_id += 1

    Path(output_json).write_text(
        json.dumps(coco, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    print(f"COCO JSON 已保存: {output_json}")
    print(f"  图片数: {len(coco['images'])}")
    print(f"  标注数: {len(coco['annotations'])}")


def coco_to_yolo(
    coco_json: str,
    images_dir: str,
    output_labels_dir: str,
) -> Dict[int, str]:
    """
    COCO JSON 格式标注转 YOLO 格式

    Returns
    -------
    Dict[int, str] : 类别映射 {id: name}
    """
    with open(coco_json, "r", encoding="utf-8") as f:
        coco = json.load(f)

    out_dir = Path(output_labels_dir)
    out_dir.mkdir(parents=True, exist_ok=True)

    # 构建映射
    cat_map = {c["id"]: c["name"] for c in coco["categories"]}
    img_map = {img["id"]: img for img in coco["images"]}

    # 按图片分组标注
    img_anns = defaultdict(list)
    for ann in coco["annotations"]:
        img_anns[ann["image_id"]].append(ann)

    for img_id, img_info in img_map.items():
        w = img_info["width"]
        h = img_info["height"]
        lbl_name = Path(img_info["file_name"]).stem + ".txt"

        lines = []
        for ann in img_anns.get(img_id, []):
            bx, by, bw, bh = ann["bbox"]
            cx = (bx + bw / 2) / w
            cy = (by + bh / 2) / h
            nw = bw / w
            nh = bh / h

            cls_id = ann["category_id"]
            lines.append(f"{cls_id} {cx:.6f} {cy:.6f} {nw:.6f} {nh:.6f}")

        (out_dir / lbl_name).write_text("\n".join(lines), encoding="utf-8")

    # 重映射类别 ID 从 0 开始
    remap = {}
    for new_id, (old_id, name) in enumerate(sorted(cat_map.items())):
        remap[new_id] = name

    print(f"YOLO 标签已保存: {output_labels_dir}")
    print(f"  图片数: {len(img_map)}")
    return remap


def split_dataset(
    images_dir: str,
    labels_dir: str,
    output_dir: str,
    train_ratio: float = 0.8,
    val_ratio: float = 0.15,
    test_ratio: float = 0.05,
    seed: int = 42,
) -> dict:
    """
    按比例拆分数据集为 train/val/test
    """
    random.seed(seed)

    img_dir = Path(images_dir)
    lbl_dir = Path(labels_dir)
    out = Path(output_dir)

    image_exts = {".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff"}
    all_images = [f for f in sorted(img_dir.iterdir()) if f.suffix.lower() in image_exts]
    random.shuffle(all_images)

    n = len(all_images)
    n_train = int(n * train_ratio)
    n_val = int(n * val_ratio)

    splits = {
        "train": all_images[:n_train],
        "val": all_images[n_train : n_train + n_val],
        "test": all_images[n_train + n_val :],
    }

    stats = {}
    for split_name, files in splits.items():
        if not files:
            continue

        img_out = out / "images" / split_name
        lbl_out = out / "labels" / split_name
        img_out.mkdir(parents=True, exist_ok=True)
        lbl_out.mkdir(parents=True, exist_ok=True)

        for img_file in files:
            shutil.copy2(img_file, img_out / img_file.name)

            lbl_file = lbl_dir / (img_file.stem + ".txt")
            if lbl_file.exists():
                shutil.copy2(lbl_file, lbl_out / lbl_file.name)

        stats[split_name] = len(files)

    print(f"数据集拆分完成: {stats}")
    return stats


def main():
    import argparse

    parser = argparse.ArgumentParser(description="数据集工具")
    sub = parser.add_subparsers(dest="command")

    # analyze
    a = sub.add_parser("analyze", help="分析数据集")
    a.add_argument("--data", required=True, help="data.yaml 路径")
    a.add_argument("--output", default="dataset_stats.json")

    # yolo2coco
    y2c = sub.add_parser("yolo2coco", help="YOLO → COCO")
    y2c.add_argument("--images", required=True)
    y2c.add_argument("--labels", required=True)
    y2c.add_argument("--names", required=True, help="JSON: {0: 'cls0', 1: 'cls1'}")
    y2c.add_argument("--output", default="coco_annotations.json")

    # coco2yolo
    c2y = sub.add_parser("coco2yolo", help="COCO → YOLO")
    c2y.add_argument("--coco", required=True, help="COCO JSON 路径")
    c2y.add_argument("--images", required=True)
    c2y.add_argument("--output", default="yolo_labels")

    # split
    s = sub.add_parser("split", help="拆分数据集")
    s.add_argument("--images", required=True)
    s.add_argument("--labels", required=True)
    s.add_argument("--output", required=True)
    s.add_argument("--train", type=float, default=0.8)
    s.add_argument("--val", type=float, default=0.15)
    s.add_argument("--test", type=float, default=0.05)

    args = parser.parse_args()

    if args.command == "analyze":
        stats = analyze_dataset(args.data)
        Path(args.output).write_text(json.dumps(stats, ensure_ascii=False, indent=2))
        print(json.dumps(stats, ensure_ascii=False, indent=2))

    elif args.command == "yolo2coco":
        names = json.loads(args.names)
        names = {int(k): v for k, v in names.items()}
        yolo_to_coco(args.images, args.labels, names, args.output)

    elif args.command == "coco2yolo":
        coco_to_yolo(args.coco, args.images, args.output)

    elif args.command == "split":
        split_dataset(args.images, args.labels, args.output, args.train, args.val, args.test)

    else:
        parser.print_help()


if __name__ == "__main__":
    main()